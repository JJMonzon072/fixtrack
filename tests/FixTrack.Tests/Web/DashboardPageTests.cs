using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Tests.Web;

/// <summary>Cola de trabajo con datos reales (SQLite en memoria).</summary>
public sealed class DashboardPageTests(FixTrackWebApplicationFactory factory)
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

}
