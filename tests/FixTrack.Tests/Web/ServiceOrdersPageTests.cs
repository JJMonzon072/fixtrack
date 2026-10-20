using System.Net;
using System.Text.RegularExpressions;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Módulo Órdenes de servicio de punta a punta sobre SQLite en memoria.</summary>
public sealed partial class ServiceOrdersPageTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Scenario(int CustomerId, int DeviceId, int TechnicianId);

    /// <summary>Cliente con un equipo y un técnico activo, con nombres únicos para cada prueba.</summary>
    private static async Task<Scenario> ScenarioAsync(HttpClient client, string tag)
    {
        var customerId = await FormPost.CreateCustomerAsync(client, $"Cliente {tag}", "5555-7000");
        var deviceId = await CreateDeviceAsync(client, customerId, $"SN-{tag}");
        var technicianId = FormPost.CreatedId(await FormPost.SendAsync(client, "/tecnicos/nuevo", new()
        {
            ["FullName"] = $"Tecnico {tag}",
            ["Specialty"] = "Laptops y equipos de escritorio",
        }));
        return new Scenario(customerId, deviceId, technicianId);
    }

    private static async Task<int> CreateDeviceAsync(HttpClient client, int customerId, string serial) =>
        FormPost.CreatedId(await FormPost.SendAsync(client, "/equipos/nuevo", new()
        {
            ["CustomerId"] = customerId.ToString(),
            ["Type"] = "1",
            ["Brand"] = "Dell",
            ["Model"] = "Latitude 5440",
            ["SerialNumber"] = serial,
        }));

    private static Dictionary<string, string> OrderForm(Scenario scenario, int? technicianId = null) => new()
    {
        ["CustomerId"] = scenario.CustomerId.ToString(),
        ["DeviceId"] = scenario.DeviceId.ToString(),
        ["ReportedIssue"] = "No enciende después de una descarga eléctrica.",
        ["IntakeNotes"] = "Se recibe con cargador.",
        ["Priority"] = ((int)ServiceOrderPriority.High).ToString(),
        ["TechnicianId"] = technicianId?.ToString() ?? string.Empty,
        ["PromisedAt"] = string.Empty,
        ["EstimatedCost"] = "1,250.50",
    };

    private static async Task<int> CreateOrderAsync(HttpClient client, Scenario scenario, int? technicianId = null) =>
        FormPost.CreatedId(await FormPost.SendAsync(client, "/ordenes/nueva", OrderForm(scenario, technicianId)));

    private async Task<ServiceOrder> LoadAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().ServiceOrders
            .Include(order => order.StatusHistory)
            .AsNoTracking()
            .SingleAsync(order => order.Id == id, Token);
    }

    [Fact]
    public async Task Registers_an_order_and_shows_its_details_and_first_history_entry()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Registro");

        var response = await FormPost.SendAsync(client, "/ordenes/nueva", OrderForm(scenario, scenario.TechnicianId));
        var html = await response.Content.ReadAsStringAsync(Token);
        var id = FormPost.CreatedId(response);

        Assert.Matches(@"/ordenes/\d+$", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains($"Orden OS-{id:D6} registrada correctamente.", html);
        Assert.Contains($">OS-{id:D6}</h1>", html);
        Assert.Contains("No enciende después de una descarga eléctrica.", html);
        Assert.Contains("Q 1,250.50", html);
        Assert.Contains("Tecnico Registro", html);
        Assert.Contains("Orden registrada.", html);

        var order = await LoadAsync(id);
        Assert.Equal(ServiceOrderStatus.Received, order.Status);
        Assert.Equal(1250.50m, order.EstimatedCost);
        Assert.Equal(scenario.TechnicianId, order.TechnicianId);
        Assert.Single(order.StatusHistory);
    }

    [Fact]
    public async Task Shows_every_validation_error_in_spanish()
    {
        using var client = factory.CreateClient();

        var html = await (await FormPost.SendAsync(client, "/ordenes/nueva", new()
        {
            ["CustomerId"] = string.Empty,
            ["DeviceId"] = string.Empty,
            ["ReportedIssue"] = " ",
            ["Priority"] = string.Empty,
            ["EstimatedCost"] = "-5",
        })).Content.ReadAsStringAsync(Token);

        Assert.Contains("Revisa los siguientes 5 campos", html);
        Assert.Contains("Selecciona el cliente que entrega el equipo.", html);
        Assert.Contains("Selecciona el equipo que se recibe.", html);
        Assert.Contains("Describe el problema reportado por el cliente.", html);
        Assert.Contains("Selecciona la prioridad.", html);
        Assert.Contains("El costo estimado no puede ser negativo.", html);
    }

    [Theory]
    [InlineData("PromisedAt", "31/12/2026", "Escribe una fecha y hora válidas.")]
    [InlineData("EstimatedCost", "doscientos", "Escribe el monto solo con números")]
    [InlineData("EstimatedCost", "10.555", "El costo estimado admite como máximo dos decimales.")]
    public async Task Rejects_dates_and_amounts_that_cannot_be_interpreted(string field, string value, string expected)
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, $"Formato{field}{value.Length}");
        var form = OrderForm(scenario);
        form[field] = value;

        var html = await (await FormPost.SendAsync(client, "/ordenes/nueva", form)).Content.ReadAsStringAsync(Token);

        Assert.Contains(expected, html);
        Assert.DoesNotContain("registrada correctamente", html);
    }

    [Fact]
    public async Task The_device_must_belong_to_the_selected_customer()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Dueno");
        var otherCustomer = await FormPost.CreateCustomerAsync(client, "Otro Dueño", "5555-7001");

        var html = await (await FormPost.SendAsync(client, "/ordenes/nueva", OrderForm(scenario with { CustomerId = otherCustomer })))
            .Content.ReadAsStringAsync(Token);

        Assert.Contains("El equipo seleccionado no pertenece a este cliente.", html);
    }

    [Fact]
    public async Task Orders_cannot_be_registered_for_an_inactive_customer_or_with_an_inactive_technician()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Inactivos");
        await FormPost.SendAsync(client, $"/tecnicos/{scenario.TechnicianId}/desactivar", []);

        var withInactiveTechnician = await (await FormPost.SendAsync(client, "/ordenes/nueva", OrderForm(scenario, scenario.TechnicianId)))
            .Content.ReadAsStringAsync(Token);
        await FormPost.SendAsync(client, $"/clientes/{scenario.CustomerId}/desactivar", []);
        var withInactiveCustomer = await (await FormPost.SendAsync(client, "/ordenes/nueva", OrderForm(scenario)))
            .Content.ReadAsStringAsync(Token);
        var preselected = await client.GetStringAsync($"/ordenes/nueva?equipo={scenario.DeviceId}", Token);

        Assert.Contains("El técnico seleccionado está inactivo y no puede recibir asignaciones.", withInactiveTechnician);
        Assert.Contains("El cliente está inactivo. Reactívalo para registrarle órdenes nuevas.", withInactiveCustomer);
        Assert.Contains("está inactivo y no admite órdenes nuevas", preselected);
    }

    [Fact]
    public async Task The_order_number_and_status_cannot_be_chosen_in_the_form()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Sobreescritura");
        var form = OrderForm(scenario);
        form["Id"] = "999";
        form["Number"] = "OS-999999";
        form["Status"] = ((int)ServiceOrderStatus.Delivered).ToString();
        form["FinalCost"] = "1";

        var id = FormPost.CreatedId(await FormPost.SendAsync(client, "/ordenes/nueva", form));

        var order = await LoadAsync(id);
        Assert.NotEqual(999, id);
        Assert.Equal(ServiceOrderStatus.Received, order.Status);
        Assert.Null(order.FinalCost);
    }

    [Fact]
    public async Task A_technician_with_open_orders_cannot_be_deactivated()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Carga");
        var id = await CreateOrderAsync(client, scenario, scenario.TechnicianId);

        var html = await (await FormPost.SendAsync(client, $"/tecnicos/{scenario.TechnicianId}/desactivar", [])).Content.ReadAsStringAsync(Token);

        Assert.Contains("No se puede desactivar: el técnico tiene 1 orden abierta asignada.", html);
        Assert.Contains($"OS-{id:D6}", html);
    }

    [Fact]
    public async Task Lists_filters_and_finds_orders_by_number()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Listado");
        var unassigned = await CreateOrderAsync(client, scenario);
        var assigned = await CreateOrderAsync(client, scenario, scenario.TechnicianId);

        var byNumber = await client.GetStringAsync($"/ordenes?buscar=OS-{assigned:D6}&vista=todas", Token);
        var withoutTechnician = await client.GetStringAsync("/ordenes?tecnico=sin-asignar&buscar=Cliente%20Listado", Token);
        var byTechnician = await client.GetStringAsync($"/ordenes?tecnico={scenario.TechnicianId}", Token);

        Assert.Equal([$"OS-{assigned:D6}"], OrderNumbers(byNumber));
        Assert.Equal([$"OS-{unassigned:D6}"], OrderNumbers(withoutTechnician));
        Assert.Equal([$"OS-{assigned:D6}"], OrderNumbers(byTechnician));
    }

    [Fact]
    public async Task Overdue_orders_are_flagged_and_listed_in_their_own_view()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Atraso");
        var form = OrderForm(scenario);
        form["PromisedAt"] = DateTime.Now.AddHours(2).ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var id = FormPost.CreatedId(await FormPost.SendAsync(client, "/ordenes/nueva", form));

        // Tres días después, la misma orden ya está vencida.
        var later = new MutableClock(DateTimeOffset.UtcNow.AddDays(3));
        using var futureClient = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(later)))
            .CreateClient();
        var overdue = await futureClient.GetStringAsync("/ordenes?vista=vencidas&buscar=Cliente%20Atraso", Token);
        var details = await futureClient.GetStringAsync($"/ordenes/{id}", Token);

        Assert.Equal([$"OS-{id:D6}"], OrderNumbers(overdue));
        Assert.Contains("<tr class=\"is-overdue\">", overdue);
        Assert.Contains("Vencida hace", overdue);
        Assert.Contains("Entrega vencida", details);
    }

    [Fact]
    public async Task The_history_cannot_be_edited_or_deleted_through_the_application()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Historial");
        var id = await CreateOrderAsync(client, scenario);

        foreach (var url in new[] { $"/ordenes/{id}/historial", $"/ordenes/{id}/historial/1/editar", $"/ordenes/{id}/eliminar" })
        {
            // No existe ninguna ruta: la aplicación responde 404 (GET) o 405 (POST) y no cambia nada.
            using var response = await FormPost.SendAsync(client, url, []);
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        }

        Assert.Single((await LoadAsync(id)).StatusHistory);
    }

    [Fact]
    public async Task Customer_devices_are_offered_as_json_for_the_order_form()
    {
        using var client = factory.CreateClient();
        var scenario = await ScenarioAsync(client, "Json");

        var json = await client.GetStringAsync($"/clientes/{scenario.CustomerId}/equipos", Token);
        using var missing = await client.GetAsync("/clientes/999999/equipos", Token);

        Assert.Contains($"\"id\":{scenario.DeviceId}", json);
        Assert.Contains("\"title\":\"Dell Latitude 5440\"", json);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("/ordenes/999999")]
    [InlineData("/ordenes/999999/estado")]
    [InlineData("/ordenes/999999/asignar")]
    [InlineData("/ordenes/999999/diagnostico")]
    [InlineData("/ordenes/999999/planificacion")]
    public async Task Unknown_orders_return_not_found(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string[] OrderNumbers(string html) =>
        [.. OrderLink().Matches(html).Select(match => match.Groups["number"].Value)];

    [GeneratedRegex("class=\"order-ref__number row-link\" href=\"/ordenes/\\d+\">(?<number>OS-\\d{6})</a>")]
    private static partial Regex OrderLink();

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
