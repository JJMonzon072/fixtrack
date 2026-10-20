using System.Text.RegularExpressions;
using FixTrack.Domain.ServiceOrders;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Panel general y cola de trabajo con datos reales (SQLite en memoria).</summary>
public sealed partial class DashboardPageTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(int Customer, int Device, int Technician)> ScenarioAsync(HttpClient client, string tag)
    {
        var customerId = await FormPost.CreateCustomerAsync(client, $"Cliente {tag}", "5555-8000");
        var deviceId = FormPost.CreatedId(await FormPost.SendAsync(client, "/equipos/nuevo", new()
        {
            ["CustomerId"] = customerId.ToString(),
            ["Type"] = "2",
            ["Brand"] = "Lenovo",
            ["Model"] = "ThinkCentre",
            ["SerialNumber"] = $"LN-{tag}",
        }));
        var technicianId = FormPost.CreatedId(await FormPost.SendAsync(client, "/tecnicos/nuevo", new()
        {
            ["FullName"] = $"Tecnico {tag}",
            ["Specialty"] = "Equipos de escritorio",
        }));
        return (customerId, deviceId, technicianId);
    }

    private static async Task<int> CreateOrderAsync(HttpClient client, (int Customer, int Device, int Technician) scenario, ServiceOrderPriority priority, bool assign, string promisedAt = "")
    {
        return FormPost.CreatedId(await FormPost.SendAsync(client, "/ordenes/nueva", new()
        {
            ["CustomerId"] = scenario.Customer.ToString(),
            ["DeviceId"] = scenario.Device.ToString(),
            ["ReportedIssue"] = "No enciende.",
            ["Priority"] = ((int)priority).ToString(),
            ["TechnicianId"] = assign ? scenario.Technician.ToString() : string.Empty,
            ["PromisedAt"] = promisedAt,
            ["EstimatedCost"] = string.Empty,
        }));
    }

    [Fact]
    public async Task Without_orders_every_indicator_is_zero_and_the_lists_explain_why_they_are_empty()
    {
        await using var emptyFactory = new FixTrackWebApplicationFactory();
        using var client = emptyFactory.CreateClient();

        var dashboard = await client.GetStringAsync("/", Token);
        var queue = await client.GetStringAsync("/cola-de-trabajo", Token);

        Assert.All(MetricValue().Matches(dashboard), match => Assert.Equal("0", match.Groups["value"].Value));
        Assert.Contains("No hay órdenes abiertas. Cuando registres una", dashboard);
        Assert.Contains("No hay entregas comprometidas pendientes.", dashboard);
        Assert.Contains("Puesta en marcha", dashboard);
        Assert.Contains("No hay órdenes abiertas", queue);
        Assert.Contains("Cómo se calcula la puntuación", queue);
    }

    [Fact]
    public async Task The_dashboard_counts_real_orders_and_recommends_the_best_scored_actions()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Panel");
        var urgent = await CreateOrderAsync(client, scenario, ServiceOrderPriority.Urgent, assign: true);
        var unassigned = await CreateOrderAsync(client, scenario, ServiceOrderPriority.Low, assign: false);

        var html = await client.GetStringAsync("/", Token);
        var values = MetricValue().Matches(html).Select(match => int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

        Assert.True(values[0] >= 2, "Órdenes abiertas");
        Assert.True(values[4] >= 1, "Sin técnico");
        Assert.True(values[5] >= 2, "Recibidas recientemente");
        Assert.Contains($"href=\"/ordenes/{urgent}/estado?a=en-diagnostico\"", html);
        Assert.Contains($"href=\"/ordenes/{unassigned}/asignar\"", html);
        Assert.Contains("Iniciar diagnóstico", html);
        Assert.Contains("Asignar técnico", html);
        Assert.Contains("Órdenes abiertas por estado", html);
        Assert.Contains("Carga de los técnicos", html);
    }

    [Fact]
    public async Task The_work_queue_orders_repairs_by_score_and_explains_each_score()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Cola");
        var normal = await CreateOrderAsync(client, scenario, ServiceOrderPriority.Normal, assign: true);
        var urgent = await CreateOrderAsync(client, scenario, ServiceOrderPriority.Urgent, assign: true);

        var html = await client.GetStringAsync("/cola-de-trabajo", Token);
        var urgentPosition = html.IndexOf($">OS-{urgent:D6}<", StringComparison.Ordinal);
        var normalPosition = html.IndexOf($">OS-{normal:D6}<", StringComparison.Ordinal);

        Assert.True(urgentPosition > 0 && normalPosition > urgentPosition, "La orden urgente debe aparecer antes que la normal.");
        Assert.Contains("Prioridad urgente <span class=\"mono\">+90</span>", html);
        Assert.Contains("Reparaciones", html);
        Assert.Contains("Listas para entrega", html);
        Assert.Contains("En espera de repuestos", html);
    }

    [Fact]
    public async Task Overdue_orders_add_their_bonus_in_the_queue_and_count_on_the_dashboard()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Vencida");
        var promised = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var id = await CreateOrderAsync(client, scenario, ServiceOrderPriority.High, assign: true, promisedAt: promised);

        using var later = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new LaterClock(DateTimeOffset.UtcNow.AddDays(2)))))
            .CreateClient();
        var queue = await later.GetStringAsync("/cola-de-trabajo", Token);
        var dashboard = await later.GetStringAsync("/", Token);

        Assert.Contains("Entrega vencida <span class=\"mono\">+50</span>", queue);
        Assert.Contains($">OS-{id:D6}<", queue);
        Assert.Contains("metric metric--alert", dashboard);
    }

    [GeneratedRegex("<dd class=\"metric__value\">(?<value>[^<]*)</dd>")]
    private static partial Regex MetricValue();

    private sealed class LaterClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
