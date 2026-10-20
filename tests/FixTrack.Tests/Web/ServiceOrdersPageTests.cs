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

    private static Task<HttpResponseMessage> ChangeStatusAsync(HttpClient client, int id, ServiceOrderStatus status, int version, string note = "", string finalCost = "") =>
        FormPost.SendAsync(client, $"/ordenes/{id}/estado", new()
        {
            ["Status"] = ((int)status).ToString(),
            ["Note"] = note,
            ["FinalCost"] = finalCost,
            ["Version"] = version.ToString(),
        });

    [Fact]
    public async Task Registers_an_order_and_shows_its_details_and_first_history_entry()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
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
        using var client = await factory.CreateAuthenticatedClientAsync();

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
        using var client = await factory.CreateAuthenticatedClientAsync();
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
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Dueno");
        var otherCustomer = await FormPost.CreateCustomerAsync(client, "Otro Dueño", "5555-7001");

        var html = await (await FormPost.SendAsync(client, "/ordenes/nueva", OrderForm(scenario with { CustomerId = otherCustomer })))
            .Content.ReadAsStringAsync(Token);

        Assert.Contains("El equipo seleccionado no pertenece a este cliente.", html);
    }

    [Fact]
    public async Task Orders_cannot_be_registered_for_an_inactive_customer_or_with_an_inactive_technician()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
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
        using var client = await factory.CreateAuthenticatedClientAsync();
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
    public async Task Walks_an_order_through_the_whole_workflow_and_closes_it()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Flujo");
        var id = await CreateOrderAsync(client, scenario);

        var assigned = await FormPost.SendAsync(client, $"/ordenes/{id}/asignar", new() { ["TechnicianId"] = scenario.TechnicianId.ToString(), ["Version"] = "0" });
        await ChangeStatusAsync(client, id, ServiceOrderStatus.Diagnosing, 1);
        var diagnosed = await FormPost.SendAsync(client, $"/ordenes/{id}/diagnostico", new() { ["Diagnosis"] = "Fuente de poder dañada.", ["Version"] = "2" });
        await ChangeStatusAsync(client, id, ServiceOrderStatus.InRepair, 3);
        await ChangeStatusAsync(client, id, ServiceOrderStatus.AwaitingParts, 4, note: "Fuente de 65 W pedida al proveedor");
        await ChangeStatusAsync(client, id, ServiceOrderStatus.InRepair, 5);
        await ChangeStatusAsync(client, id, ServiceOrderStatus.ReadyForPickup, 6, note: "Probada durante dos horas");
        var delivered = await ChangeStatusAsync(client, id, ServiceOrderStatus.Delivered, 7, note: "Pagado en efectivo", finalCost: "1300");
        var html = await delivered.Content.ReadAsStringAsync(Token);

        Assert.Contains("Técnico asignado correctamente.", await assigned.Content.ReadAsStringAsync(Token));
        Assert.Contains("Diagnóstico guardado.", await diagnosed.Content.ReadAsStringAsync(Token));
        Assert.Contains("La orden pasó a «Entregada».", html);
        Assert.Contains("La orden está cerrada y ya no admite cambios.", html);
        Assert.Contains("Fuente de 65 W pedida al proveedor", html);
        Assert.Contains("&#x2B;Q 49.50", html); // "+Q 49.50": el codificador HTML escapa el signo +.

        var order = await LoadAsync(id);
        Assert.Equal(ServiceOrderStatus.Delivered, order.Status);
        Assert.Equal(1300m, order.FinalCost);
        Assert.Equal(7, order.StatusHistory.Count);
        Assert.NotNull(order.DeliveredAtUtc);
    }

    [Fact]
    public async Task Rejected_transitions_are_explained_on_the_status_page()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Rechazo");
        var id = await CreateOrderAsync(client, scenario);

        var skip = await (await ChangeStatusAsync(client, id, ServiceOrderStatus.Delivered, 0, finalCost: "100")).Content.ReadAsStringAsync(Token);
        var withoutTechnician = await (await ChangeStatusAsync(client, id, ServiceOrderStatus.Diagnosing, 0)).Content.ReadAsStringAsync(Token);
        var page = await client.GetStringAsync($"/ordenes/{id}/estado", Token);

        Assert.Contains("Una orden «Recibida» no puede pasar a «Entregada».", skip);
        Assert.Contains("Asigna un técnico antes de iniciar el diagnóstico.", withoutTechnician);
        Assert.Contains("Asigna un técnico antes de iniciar el diagnóstico.", page);
        Assert.Equal(ServiceOrderStatus.Received, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task A_stale_form_does_not_overwrite_a_newer_change()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Concurrencia");
        var id = await CreateOrderAsync(client, scenario);
        await FormPost.SendAsync(client, $"/ordenes/{id}/planificacion", new() { ["Priority"] = "4", ["Version"] = "0" });

        var html = await (await FormPost.SendAsync(client, $"/ordenes/{id}/planificacion", new() { ["Priority"] = "1", ["Version"] = "0" }))
            .Content.ReadAsStringAsync(Token);

        Assert.Contains("Otra persona modificó esta orden mientras la revisabas.", html);
        Assert.Equal(ServiceOrderPriority.Urgent, (await LoadAsync(id)).Priority);
    }

    [Fact]
    public async Task Closed_orders_do_not_offer_their_forms()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Cerrada");
        var id = await CreateOrderAsync(client, scenario);
        await ChangeStatusAsync(client, id, ServiceOrderStatus.Cancelled, 0, note: "El cliente desistió");

        var form = await client.GetStringAsync($"/ordenes/{id}/planificacion", Token);
        var post = await (await ChangeStatusAsync(client, id, ServiceOrderStatus.Diagnosing, 1)).Content.ReadAsStringAsync(Token);

        Assert.Contains("La orden está cerrada (entregada o cancelada) y ya no admite cambios.", form);
        Assert.Contains("Motivo: El cliente desistió", form);
        Assert.Contains("ya no admite cambios", post);
    }

    [Fact]
    public async Task A_technician_with_open_orders_cannot_be_deactivated()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Carga");
        var id = await CreateOrderAsync(client, scenario, scenario.TechnicianId);

        var html = await (await FormPost.SendAsync(client, $"/tecnicos/{scenario.TechnicianId}/desactivar", [])).Content.ReadAsStringAsync(Token);

        Assert.Contains("No se puede desactivar: el técnico tiene 1 orden abierta asignada.", html);
        Assert.Contains($"OS-{id:D6}", html);
    }

    [Fact]
    public async Task Lists_filters_and_finds_orders_by_number()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
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
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Atraso");
        var form = OrderForm(scenario);
        form["PromisedAt"] = DateTime.Now.AddHours(2).ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var id = FormPost.CreatedId(await FormPost.SendAsync(client, "/ordenes/nueva", form));

        // Tres días después, la misma orden ya está vencida.
        var later = new MutableClock(DateTimeOffset.UtcNow.AddDays(3));
        using var futureClient = await factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(later)))
            .CreateAuthenticatedClientAsync();
        var overdue = await futureClient.GetStringAsync("/ordenes?vista=vencidas&buscar=Cliente%20Atraso", Token);
        var details = await futureClient.GetStringAsync($"/ordenes/{id}", Token);

        Assert.Equal([$"OS-{id:D6}"], OrderNumbers(overdue));
        Assert.Contains("<tr class=\"is-overdue\">", overdue);
        Assert.Contains("Vencida hace", overdue);
        Assert.Contains("Entrega vencida", details);
    }

    [Fact]
    public async Task Customer_device_and_technician_pages_show_their_real_orders()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Integracion");
        var id = await CreateOrderAsync(client, scenario, scenario.TechnicianId);
        var number = $"OS-{id:D6}";

        var customer = await client.GetStringAsync($"/clientes/{scenario.CustomerId}", Token);
        var device = await client.GetStringAsync($"/equipos/{scenario.DeviceId}", Token);
        var technician = await client.GetStringAsync($"/tecnicos/{scenario.TechnicianId}", Token);
        var filtered = await client.GetStringAsync($"/ordenes?cliente={scenario.CustomerId}&vista=todas", Token);
        var newFromDevice = await client.GetStringAsync($"/ordenes/nueva?equipo={scenario.DeviceId}", Token);

        Assert.Contains(number, customer);
        Assert.Contains($"href=\"/ordenes/nueva?cliente={scenario.CustomerId}\"", customer);
        Assert.Contains(number, device);
        Assert.Contains($"href=\"/ordenes/nueva?equipo={scenario.DeviceId}\"", device);
        Assert.Contains(number, technician);
        Assert.Contains("Ver todas sus órdenes", technician);
        Assert.Equal([number], OrderNumbers(filtered));
        Assert.Contains("Cliente: Cliente Integracion", filtered);
        Assert.Contains($"name=\"DeviceId\" value=\"{scenario.DeviceId}\"", newFromDevice);
        Assert.Contains("Elegir otro equipo de este cliente", newFromDevice);
    }

    [Fact]
    public async Task Rejects_posts_without_an_antiforgery_token()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "Token");
        var id = await CreateOrderAsync(client, scenario);

        using var response = await client.PostAsync(
            $"/ordenes/{id}/estado",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "7", ["Note"] = "x", ["Version"] = "0" }),
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ServiceOrderStatus.Received, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task The_history_cannot_be_edited_or_deleted_through_the_application()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
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
        using var client = await factory.CreateAuthenticatedClientAsync();
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
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_reassignment_appears_in_the_timeline_with_its_author_and_reason()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "reasignacion");
        var id = await CreateOrderAsync(client, scenario, scenario.TechnicianId);
        var second = FormPost.CreatedId(await FormPost.SendAsync(client, "/tecnicos/nuevo", new()
        {
            ["FullName"] = "Andrea Reasignada",
            ["Specialty"] = "Impresoras",
        }));
        var order = await LoadAsync(id);

        var form = await client.GetStringAsync($"/ordenes/{id}/asignar", Token);
        using var response = await FormPost.SendAsync(client, $"/ordenes/{id}/asignar", new()
        {
            ["TechnicianId"] = second.ToString(),
            ["Note"] = "El técnico anterior está incapacitado",
            ["Version"] = order.Version.ToString(),
        });
        var details = await response.Content.ReadAsStringAsync(Token);

        Assert.Contains("Motivo de la reasignación", form);
        Assert.Contains("Técnico asignado: Tecnico reasignacion", details);
        Assert.Contains("Reasignada a Andrea Reasignada", details);
        Assert.Contains("antes: Tecnico reasignacion", details);
        Assert.Contains("El técnico anterior está incapacitado", details);
        Assert.Contains($"por {TestAccounts.Administrator.FullName}", details);
        Assert.Contains("Historial de la orden", details);

        await using var scope = factory.Services.CreateAsyncScope();
        var assignments = await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().ServiceOrderAssignments
            .Where(assignment => assignment.ServiceOrderId == id)
            .OrderBy(assignment => assignment.Id)
            .ToListAsync(Token);
        Assert.Equal([(null, scenario.TechnicianId), (scenario.TechnicianId, second)], assignments.Select(a => (a.FromTechnicianId, a.ToTechnicianId)));
        Assert.All(assignments, assignment => Assert.Equal(TestAccounts.Administrator.FullName, assignment.AssignedByName));
    }

    [Fact]
    public async Task Status_changes_show_who_made_them()
    {
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);
        var scenario = await ScenarioAsyncAsAdministratorAsync("autor");
        var id = await CreateOrderAsync(client, scenario, scenario.TechnicianId);

        using var response = await ChangeStatusAsync(client, id, ServiceOrderStatus.Diagnosing, (await LoadAsync(id)).Version);
        var details = await response.Content.ReadAsStringAsync(Token);
        var history = (await LoadAsync(id)).StatusHistory.OrderBy(change => change.Id).ToList();

        Assert.Contains($"por {TestAccounts.Operator.FullName}", details);
        Assert.All(history, change => Assert.Equal(TestAccounts.Operator.FullName, change.ChangedByName));
        Assert.All(history, change => Assert.False(string.IsNullOrEmpty(change.ChangedByUserId)));
    }

    /// <summary>El operador no registra técnicos: el escenario lo prepara un administrador.</summary>
    private async Task<Scenario> ScenarioAsyncAsAdministratorAsync(string tag)
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        return await ScenarioAsync(admin, tag);
    }

    [Fact]
    public async Task A_double_submitted_order_form_registers_one_order()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "doble-envio");
        var page = await client.GetStringAsync($"/ordenes/nueva?cliente={scenario.CustomerId}", Token);
        var requestId = Regex.Match(page, "name=\"RequestId\" value=\"(?<id>[0-9a-f-]{36})\"").Groups["id"].Value;
        var form = OrderForm(scenario);
        form["RequestId"] = requestId;

        using var first = await FormPost.SendAsync(client, "/ordenes/nueva", new(form));
        using var second = await FormPost.SendAsync(client, "/ordenes/nueva", new(form));

        Assert.NotEmpty(requestId);
        Assert.Equal(first.RequestMessage!.RequestUri, second.RequestMessage!.RequestUri);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().ServiceOrders.CountAsync(order => order.DeviceId == scenario.DeviceId, Token));
    }

    [Fact]
    public async Task Deactivating_a_customer_with_open_orders_warns_how_many_remain()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var scenario = await ScenarioAsync(client, "cliente-con-ordenes");
        await CreateOrderAsync(client, scenario);
        var withoutOrders = await FormPost.CreateCustomerAsync(client, "Cliente Sin Ordenes", "5555-7400");

        var details = await client.GetStringAsync($"/clientes/{scenario.CustomerId}", Token);
        var empty = await client.GetStringAsync($"/clientes/{withoutOrders}", Token);

        Assert.Contains("Tiene 1 orden abierta, que seguirá", details);
        Assert.DoesNotContain("deactivate-open-orders", empty);
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
