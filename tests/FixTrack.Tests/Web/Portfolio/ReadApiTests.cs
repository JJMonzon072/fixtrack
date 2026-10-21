using System.Net;
using System.Text.Json;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>
/// API REST v1 de solo lectura con la aplicación real. Órdenes de <c>ReportSeed</c>: A=1 … K=11 (ver ServiceSheetsTests).
/// </summary>
public sealed class ReadApiTests(PortfolioExtrasFactory factory) : IClassFixture<PortfolioExtrasFactory>
{
    public static TheoryData<string> Endpoints { get; } = ["/api/v1/ordenes", "/api/v1/ordenes/1", "/api/v1/equipos", "/api/v1/metricas"];

    /// <summary>
    /// Solo las órdenes del escenario: las que agregan algunas pruebas se reciben el 3 de noviembre (reloj fijo) y así no
    /// cambian los resultados de las demás, sin importar el orden de ejecución.
    /// </summary>
    private const string Scenario = "recibidaHasta=2026-11-02";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(TestAccount? account = null)
    {
        await factory.SeedReportsAsync();
        return await factory.CreateAuthenticatedClientAsync(account);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Json)> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        return (response, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static List<int> Ids(JsonElement page) => [.. page.GetProperty("datos").EnumerateArray().Select(item => item.GetProperty("id").GetInt32())];

    private static HashSet<string> PropertyNames(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Without_a_session_every_endpoint_answers_401_problem_json(string url)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(url, Token);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
        Assert.Equal("Se requiere iniciar sesión", problem.GetProperty("title").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task The_read_only_role_cannot_use_the_api_even_with_get(string url)
    {
        using var client = await ClientAsync(TestAccounts.ReadOnly);

        var (response, problem) = await GetJsonAsync(client, url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Sin permiso", problem.GetProperty("title").GetString());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Administrators_and_operators_get_json_that_is_not_cached(string url)
    {
        using var admin = await ClientAsync();
        using var worker = await ClientAsync(TestAccounts.Operator);

        var (adminResponse, _) = await GetJsonAsync(admin, url);
        var (workerResponse, _) = await GetJsonAsync(worker, url);

        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, workerResponse.StatusCode);
        Assert.Equal("application/json; charset=utf-8", adminResponse.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", adminResponse.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Orders_are_paginated_with_a_bounded_page_size()
    {
        using var client = await ClientAsync();

        var (_, first) = await GetJsonAsync(client, $"/api/v1/ordenes?porPagina=4&orden=numero&{Scenario}");
        var (_, third) = await GetJsonAsync(client, $"/api/v1/ordenes?porPagina=4&pagina=3&orden=numero&{Scenario}");
        var (_, beyond) = await GetJsonAsync(client, $"/api/v1/ordenes?porPagina=4&pagina=50&orden=numero&{Scenario}");
        var (_, defaults) = await GetJsonAsync(client, "/api/v1/ordenes");

        var pagination = first.GetProperty("paginacion");
        Assert.Equal([1, 2, 3, 4], Ids(first));
        Assert.Equal((1, 4, 11, 3), (pagination.GetProperty("pagina").GetInt32(), pagination.GetProperty("porPagina").GetInt32(),
            pagination.GetProperty("total").GetInt32(), pagination.GetProperty("totalPaginas").GetInt32()));
        Assert.Equal([9, 10, 11], Ids(third));
        Assert.Empty(Ids(beyond));
        Assert.Equal(11, beyond.GetProperty("paginacion").GetProperty("total").GetInt32());
        Assert.Equal(20, defaults.GetProperty("paginacion").GetProperty("porPagina").GetInt32());
    }

    [Theory]
    [InlineData("/api/v1/ordenes?porPagina=101", "porPagina")]
    [InlineData("/api/v1/ordenes?porPagina=0", "porPagina")]
    [InlineData("/api/v1/ordenes?porPagina=1e3", "porPagina")]
    [InlineData("/api/v1/ordenes?pagina=0", "pagina")]
    [InlineData("/api/v1/ordenes?pagina=10001", "pagina")]
    [InlineData("/api/v1/ordenes?pagina=uno", "pagina")]
    [InlineData("/api/v1/ordenes?estado=perdida", "estado")]
    [InlineData("/api/v1/ordenes?prioridad=maxima", "prioridad")]
    [InlineData("/api/v1/ordenes?tecnico=-3", "tecnico")]
    [InlineData("/api/v1/ordenes?equipo=x", "equipo")]
    [InlineData("/api/v1/ordenes?recibidaDesde=2026-13-01", "recibidaDesde")]
    [InlineData("/api/v1/ordenes?recibidaDesde=2026-10-10&recibidaHasta=2026-10-01", "recibidaHasta")]
    [InlineData("/api/v1/ordenes?orden=costo", "orden")]
    [InlineData("/api/v1/ordenes?orden=-numero;DROP TABLE", "orden")]
    [InlineData("/api/v1/equipos?tipo=nave", "tipo")]
    [InlineData("/api/v1/equipos?porPagina=1000", "porPagina")]
    [InlineData("/api/v1/metricas?desde=2026-10-15&hasta=2026-10-01", "hasta")]
    [InlineData("/api/v1/metricas?rango=siempre", "rango")]
    [InlineData("/api/v1/metricas?tecnico=999", "tecnico")]
    public async Task Invalid_parameters_answer_400_with_errors_per_parameter(string url, string parameter)
    {
        using var client = await ClientAsync();

        var (response, problem) = await GetJsonAsync(client, url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Parámetros no válidos", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(parameter, out var messages), problem.ToString());
        Assert.NotEmpty(messages[0].GetString()!);
    }

    [Fact]
    public async Task Orders_can_be_filtered()
    {
        using var client = await ClientAsync();

        var (_, delivered) = await GetJsonAsync(client, $"/api/v1/ordenes?estado=entregada&orden=numero&{Scenario}");
        var (_, urgent) = await GetJsonAsync(client, $"/api/v1/ordenes?prioridad=urgente&orden=numero&{Scenario}");
        var (_, unassigned) = await GetJsonAsync(client, $"/api/v1/ordenes?tecnico=sin-asignar&orden=numero&{Scenario}");
        var (_, firstDay) = await GetJsonAsync(client, "/api/v1/ordenes?recibidaDesde=2026-10-01&recibidaHasta=2026-10-01");
        var (_, lastDay) = await GetJsonAsync(client, "/api/v1/ordenes?recibidaDesde=2026-10-31&recibidaHasta=2026-10-31");
        var (_, device) = await GetJsonAsync(client, $"/api/v1/ordenes?equipo=1&orden=numero&{Scenario}");

        Assert.Equal([1, 2, 5, 10, 11], Ids(delivered));
        Assert.Equal([3, 9], Ids(urgent));
        Assert.Equal([3, 4], Ids(unassigned));
        Assert.Equal([1], Ids(firstDay));
        Assert.Equal([3], Ids(lastDay));
        Assert.Equal([1, 3, 5, 8, 9], Ids(device));
    }

    [Fact]
    public async Task Orders_are_sorted_only_by_allowed_fields_with_a_stable_tie_break()
    {
        using var client = await ClientAsync();

        var (_, byPriority) = await GetJsonAsync(client, $"/api/v1/ordenes?orden=-prioridad&{Scenario}");
        var (_, byPromise) = await GetJsonAsync(client, $"/api/v1/ordenes?orden=comprometida&{Scenario}");
        var (_, newest) = await GetJsonAsync(client, $"/api/v1/ordenes?{Scenario}");

        Assert.Equal([9, 3, 8, 1, 11, 10, 7, 5, 2, 6, 4], Ids(byPriority));
        Assert.Equal([1, 5, 11, 2, 3, 4, 6, 7, 8, 9, 10], Ids(byPromise));
        Assert.Equal([4, 3, 9, 8, 6, 5, 11, 1, 2, 7, 10], Ids(newest));
    }

    [Fact]
    public async Task An_order_summary_has_exactly_the_documented_fields()
    {
        using var client = await ClientAsync();

        var (_, page) = await GetJsonAsync(client, $"/api/v1/ordenes?orden=numero&porPagina=1&{Scenario}");
        var order = page.GetProperty("datos")[0];

        Assert.Equal(["datos", "paginacion"], PropertyNames(page).Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "atrasada", "cliente", "entregaComprometidaEn", "entregadaEn", "equipo", "estado", "id", "numero", "prioridad", "recibidaEn", "tecnico" },
            PropertyNames(order).Order(StringComparer.Ordinal));
        Assert.Equal(["id", "nombre"], PropertyNames(order.GetProperty("cliente")).Order(StringComparer.Ordinal));
        Assert.Equal(["id", "nombre"], PropertyNames(order.GetProperty("tecnico")).Order(StringComparer.Ordinal));
        Assert.Equal(["id", "marca", "modelo", "tipo"], PropertyNames(order.GetProperty("equipo")).Order(StringComparer.Ordinal));
        Assert.Equal(("OS-000001", "entregada", "Entregada", "alta"), (order.GetProperty("numero").GetString(),
            order.GetProperty("estado").GetProperty("codigo").GetString(), order.GetProperty("estado").GetProperty("nombre").GetString(),
            order.GetProperty("prioridad").GetProperty("codigo").GetString()));
        Assert.Equal("2026-10-01T06:00:00Z", order.GetProperty("recibidaEn").GetString());
        Assert.Equal("2026-10-04T18:00:00Z", order.GetProperty("entregadaEn").GetString());
        Assert.False(order.GetProperty("atrasada").GetBoolean());
    }

    [Fact]
    public async Task An_order_detail_has_costs_and_history_without_authors_notes_or_contact_data()
    {
        using var client = await ClientAsync();

        var (response, order) = await GetJsonAsync(client, "/api/v1/ordenes/11");
        var history = order.GetProperty("historial").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new[]
            {
                "actualizadaEn", "atrasada", "cliente", "costos", "diagnostico", "entregaComprometidaEn", "entregadaEn", "equipo", "estado",
                "historial", "id", "numero", "observacionesRecepcion", "prioridad", "problemaReportado", "recibidaEn", "tecnico",
            },
            PropertyNames(order).Order(StringComparer.Ordinal));
        Assert.Equal(["diferencia", "estimado", "final", "moneda"], PropertyNames(order.GetProperty("costos")).Order(StringComparer.Ordinal));
        Assert.Equal((500.00m, 520.00m, 20.00m, "GTQ"), (order.GetProperty("costos").GetProperty("estimado").GetDecimal(),
            order.GetProperty("costos").GetProperty("final").GetDecimal(), order.GetProperty("costos").GetProperty("diferencia").GetDecimal(),
            order.GetProperty("costos").GetProperty("moneda").GetString()));
        Assert.Equal(["asignadoEn", "especialidad", "id", "nombre"], PropertyNames(order.GetProperty("tecnico")).Order(StringComparer.Ordinal));
        Assert.Equal("RPT-HP-1", order.GetProperty("equipo").GetProperty("numeroDeSerie").GetString());
        Assert.Equal(7, history.Count);
        Assert.All(history, entry => Assert.Equal(["estado", "estadoAnterior", "fecha"], PropertyNames(entry).Order(StringComparer.Ordinal)));
        Assert.Equal(["recibida", "en-diagnostico", "en-reparacion", "lista-para-entrega", "en-reparacion", "lista-para-entrega", "entregada"],
            history.Select(entry => entry.GetProperty("estado").GetProperty("codigo").GetString()));
        Assert.DoesNotContain("El cliente reportó otra falla", order.ToString());
    }

    [Fact]
    public async Task No_endpoint_exposes_contact_data_accounts_notes_or_audit()
    {
        using var client = await ClientAsync();

        // Una orden registrada con el formulario: su historial guarda el autor (una cuenta de Identity).
        var customer = await FormPost.CreateCustomerAsync(client, "Cliente Api Privado", "5555-9911", "privado@fixtrack.test");
        var device = FormPost.CreatedId(await FormPost.SendAsync(client, "/equipos/nuevo", new()
        {
            ["CustomerId"] = customer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Type"] = "1",
            ["Brand"] = "Acer",
            ["Model"] = "Aspire",
            ["SerialNumber"] = "API-PRIV-1",
        }));
        var order = FormPost.CreatedId(await FormPost.SendAsync(client, "/ordenes/nueva", new()
        {
            ["CustomerId"] = customer.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DeviceId"] = device.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ReportedIssue"] = "Revisión para la API",
            ["Priority"] = "2",
            ["TechnicianId"] = string.Empty,
            ["PromisedAt"] = string.Empty,
            ["EstimatedCost"] = string.Empty,
        }));

        var bodies = new List<string>();
        foreach (var url in new[] { "/api/v1/ordenes?porPagina=100", $"/api/v1/ordenes/{order}", "/api/v1/ordenes/6", "/api/v1/equipos?porPagina=100", "/api/v1/metricas?rango=anio" })
        {
            bodies.Add(await client.GetStringAsync(url, Token));
        }

        var all = string.Join('\n', bodies);
        Assert.Contains("Cliente Api Privado", all);
        foreach (var secret in new[]
        {
            "5555-9911", "privado@fixtrack.test", "5555-0101", "ana1@fixtrack.test", "carla1@fixtrack.test", "5555-0201",
            TestAccounts.Administrator.FullName, TestAccounts.Administrator.Email, "Orden registrada.", "El cliente retiró el equipo",
            "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "Version", "CreationRequestId", "auditoria", "\"telefono\"", "\"correo\"",
        })
        {
            Assert.DoesNotContain(secret, all, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Missing_orders_and_unknown_api_routes_answer_404_problem_json()
    {
        using var client = await ClientAsync();

        var (missing, problem) = await GetJsonAsync(client, "/api/v1/ordenes/9999");
        var (badId, _) = await GetJsonAsync(client, "/api/v1/ordenes/abc");
        var (unknown, _) = await GetJsonAsync(client, "/api/v1/clientes");
        var (version, _) = await GetJsonAsync(client, "/api/v2/ordenes");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Orden no encontrada", problem.GetProperty("title").GetString());
        Assert.Equal("No existe una orden con el Id 9999.", problem.GetProperty("detail").GetString());
        foreach (var response in new[] { badId, unknown, version })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.DoesNotContain("at FixTrack", problem.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST", "/api/v1/ordenes")]
    [InlineData("PUT", "/api/v1/ordenes/1")]
    [InlineData("PATCH", "/api/v1/ordenes/1")]
    [InlineData("DELETE", "/api/v1/ordenes/1")]
    [InlineData("POST", "/api/v1/equipos")]
    [InlineData("DELETE", "/api/v1/metricas")]
    public async Task Write_methods_are_rejected_with_405(string method, string url)
    {
        using var client = await ClientAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = new StringContent("{\"estado\":\"entregada\"}", System.Text.Encoding.UTF8, "application/json") };
        using var response = await client.SendAsync(request, Token);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("GET, HEAD", string.Join(", ", response.Content.Headers.Allow));
        Assert.Equal("La API v1 es de solo lectura: solo acepta GET.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Other_origins_get_no_cors_permission()
    {
        using var client = await ClientAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ordenes");
        request.Headers.Add("Origin", "https://atacante.example");
        using var response = await client.SendAsync(request, Token);
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/ordenes");
        preflight.Headers.Add("Origin", "https://atacante.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        using var preflightResponse = await client.SendAsync(preflight, Token);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.False(preflightResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.NotEqual(HttpStatusCode.OK, preflightResponse.StatusCode);
    }

    [Fact]
    public async Task Devices_are_listed_with_their_order_count_and_filters()
    {
        using var client = await ClientAsync();

        var (_, page) = await GetJsonAsync(client, "/api/v1/equipos?orden=marca&cliente=1");
        var (_, laptops) = await GetJsonAsync(client, "/api/v1/equipos?tipo=laptop");
        var device = page.GetProperty("datos")[0];

        Assert.Equal(["cliente", "id", "marca", "modelo", "numeroDeSerie", "ordenes", "registradoEn", "tipo"], PropertyNames(device).Order(StringComparer.Ordinal));
        Assert.Equal(("Dell", 5, "Ana Morales"), (device.GetProperty("marca").GetString(), device.GetProperty("ordenes").GetInt32(),
            device.GetProperty("cliente").GetProperty("nombre").GetString()));
        Assert.All(laptops.GetProperty("datos").EnumerateArray(), item => Assert.Equal("laptop", item.GetProperty("tipo").GetProperty("codigo").GetString()));
    }

    [Fact]
    public async Task Metrics_return_the_same_october_report_as_the_reports_center()
    {
        using var client = await ClientAsync();

        var (response, metrics) = await GetJsonAsync(client, "/api/v1/metricas?desde=2026-10-01&hasta=2026-10-31");
        var orders = metrics.GetProperty("ordenes");
        var amounts = metrics.GetProperty("montos");
        var period = metrics.GetProperty("periodo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((7, 4, 2), (orders.GetProperty("recibidas").GetInt32(), orders.GetProperty("entregadas").GetInt32(), orders.GetProperty("canceladas").GetInt32()));
        Assert.Equal((1420.00m, 355.00m, 20.00m, 1.7m), (amounts.GetProperty("ingresosRegistrados").GetDecimal(), amounts.GetProperty("costoPromedioPorEntrega").GetDecimal(),
            amounts.GetProperty("diferenciaFinalMenosEstimado").GetDecimal(), amounts.GetProperty("diferenciaPorcentual").GetDecimal()));
        Assert.Equal((111.0, 91.5), (metrics.GetProperty("tiempos").GetProperty("promedioHorasHastaEntrega").GetDouble(),
            metrics.GetProperty("tiempos").GetProperty("promedioHorasHastaLista").GetDouble()));
        Assert.Equal(67, metrics.GetProperty("puntualidad").GetProperty("porcentajeATiempo").GetInt32());
        Assert.Equal(("2026-10-01", "2026-10-31", "America/Guatemala", "2026-10-01T06:00:00Z", "2026-11-01T06:00:00Z", "dia"),
            (period.GetProperty("desde").GetString(), period.GetProperty("hasta").GetString(), period.GetProperty("zonaHoraria").GetString(),
             period.GetProperty("inicioUtc").GetString(), period.GetProperty("finUtc").GetString(), period.GetProperty("agrupacion").GetString()));
        Assert.Equal(31, metrics.GetProperty("serie").GetArrayLength());
        Assert.Contains("no son cobros confirmados", amounts.GetProperty("nota").GetString());
        var unassigned = metrics.GetProperty("tecnicos").EnumerateArray().Single(row => row.GetProperty("id").ValueKind == JsonValueKind.Null);
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("nombre").ValueKind);
    }

    [Fact]
    public async Task Text_with_html_characters_is_escaped_in_json()
    {
        using var client = await ClientAsync();
        int id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
            var device = await db.Devices.Include(item => item.Customer).FirstAsync(item => item.SerialNumber == "RPT-HP-1", Token);
            var order = new ServiceOrder(device, "<b>Hola</b> & 'adiós'", ServiceOrderPriority.Low, new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc), null, null, null);
            db.ServiceOrders.Add(order);
            await db.SaveChangesAsync(Token);
            id = order.Id;
        }

        var raw = await client.GetStringAsync($"/api/v1/ordenes/{id}", Token);
        var (_, json) = await GetJsonAsync(client, $"/api/v1/ordenes/{id}");

        Assert.Contains("\\u003Cb\\u003EHola\\u003C/b\\u003E \\u0026 \\u0027adiós\\u0027", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", raw, StringComparison.Ordinal);
        Assert.Equal("<b>Hola</b> & 'adiós'", json.GetProperty("problemaReportado").GetString());
    }
}
