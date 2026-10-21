using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>Centro de reportes y exportación CSV con la aplicación real (extras habilitados solo en esta prueba).</summary>
public sealed partial class ReportsPageTests(PortfolioExtrasFactory factory) : IClassFixture<PortfolioExtrasFactory>
{
    private const string October = "/reportes?desde=2026-10-01&hasta=2026-10-31";
    private const string OctoberExport = "/reportes/exportar?desde=2026-10-01&hasta=2026-10-31";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(TestAccount? account = null)
    {
        await factory.SeedReportsAsync();
        return await factory.CreateAuthenticatedClientAsync(account);
    }

    private static List<string> MetricValues(string html) =>
        [.. MetricValue().Matches(html).Select(match => Tags().Replace(match.Groups["value"].Value, string.Empty).Trim())];

    /// <summary>Página como texto: Razor codifica algunos caracteres (por ejemplo "+" como &amp;#x2B;).</summary>
    private static async Task<string> PageAsync(HttpClient client, string url) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(url, Token));

    [Fact]
    public async Task October_shows_every_indicator_with_its_own_date()
    {
        using var client = await ClientAsync();

        using var response = await client.GetAsync(October, Token);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1 class=\"page-title\">Centro de reportes</h1>", html);
        Assert.Equal(["7", "4", "2", "1 de 3", "4.6 días", "Q 1,420.00", "Q 355.00", "+1.7 %"], MetricValues(html));
        Assert.Contains("De la recepción a la entrega · lista en 3.8 días", html);
        Assert.Contains("+Q 20.00 en 3 entregas con costo estimado", html);
        Assert.Contains("2 de 3 entregas con fecha comprometida llegaron a tiempo (67 %); 1 se entregó con atraso.", html);
        Assert.Contains("Las órdenes quedan listas en 3.8 días en promedio y el cliente las retira 19.5 h después.", html);
        Assert.Contains("Los costos finales superaron lo estimado en 1.7 % (3 entregas con estimación).", html);
        Assert.Contains("2 órdenes abiertas no tienen técnico asignado en este momento.", html);
        Assert.Contains("1 oct 2026 – 31 oct 2026", html);
    }

    [Fact]
    public async Task Charts_are_accessible_svg_with_data_tables_and_no_inline_styles()
    {
        using var client = await ClientAsync();

        var html = await PageAsync(client, October);

        Assert.Equal(2, Regex.Count(html, "class=\"chart chart--ancho\""));
        Assert.Equal(2, Regex.Count(html, "class=\"chart chart--estrecho\""));
        Assert.Contains("role=\"img\" aria-labelledby=\"grafico-volumen-ancho-titulo grafico-volumen-ancho-desc\"", html);
        Assert.Contains("<desc id=\"grafico-volumen-ancho-desc\">1 oct 2026 – 31 oct 2026, por día: 7 órdenes recibidas y 4 entregadas.", html);
        Assert.Contains("<desc id=\"grafico-ingresos-ancho-desc\">1 oct 2026 – 31 oct 2026, por día: Q 1,420.00 registrados al entregar. Mayor monto: 12 oct 2026 (Q 520.00).</desc>", html);
        Assert.Equal(2, Regex.Count(html, "<summary>Ver los datos del gráfico</summary>"));
        Assert.Contains("<th scope=\"row\">12 oct 2026</th>", html);
        Assert.DoesNotContain(" style=\"", html, StringComparison.Ordinal);
        Assert.Matches(@"<link rel=""stylesheet"" href=""/css/reports\.[a-z0-9]+\.css"" />", html);
        Assert.Contains("FixTrack no registra pagos: no son cobros confirmados.", html);
    }

    [Fact]
    public async Task Technicians_priorities_and_statuses_are_listed()
    {
        using var client = await ClientAsync();

        var html = await PageAsync(client, October);
        var technicians = TechnicianRow().Matches(html[html.IndexOf("report-technicians", StringComparison.Ordinal)..])
            .Select(match => match.Groups["name"].Value.Trim())
            .ToList();

        Assert.Equal(["Carla Ruiz", "Luis Pérez", "Sin técnico asignado"], technicians);
        Assert.Contains("Q 700.00", html);
        Assert.Contains("Q 720.00", html);
        Assert.Contains("Dónde están hoy las 7 órdenes recibidas en el período.", html);
        Assert.Contains("<span class=\"bar-list__value mono\">3 <span class=\"report-bars__percent\">· 43 %</span></span>", html);
        Assert.Contains("Comparación por prioridad", html);
    }

    [Fact]
    public async Task The_sidebar_offers_reports_only_with_the_extra_enabled()
    {
        using var client = await ClientAsync();

        var reports = await PageAsync(client, "/reportes");
        var dashboard = await PageAsync(client, "/");

        Assert.Contains("<h2 class=\"nav-group__title\">Análisis</h2>", dashboard);
        Assert.Contains("<a class=\"nav-link\" aria-current=\"page\" href=\"/reportes\">", reports);
        Assert.DoesNotContain("aria-current=\"page\">\\s*<svg[^>]*><use[^>]*#chart-column", dashboard);
    }

    [Fact]
    public async Task Without_parameters_the_last_thirty_days_are_shown_and_marked()
    {
        using var client = await ClientAsync();

        var html = await PageAsync(client, "/reportes");
        var quarter = await PageAsync(client, "/reportes?rango=90d");

        Assert.Contains("5 oct 2026 – 3 nov 2026", html);
        Assert.Contains("aria-current=\"true\" href=\"/reportes?rango=30d\"", html);
        Assert.Contains("6 ago 2026 – 3 nov 2026", quarter);
        Assert.Contains("Volumen por semana, en hora local.", quarter);
    }

    [Theory]
    [InlineData("?desde=2026-10-15&hasta=2026-10-01", "La fecha final debe ser igual o posterior a la inicial.")]
    [InlineData("?desde=01/10/2026&hasta=2026-10-31", "Escribe la fecha con el formato AAAA-MM-DD.")]
    [InlineData("?desde=2026-10-01&hasta=2026-11-30", "La fecha final no puede ser posterior a hoy.")]
    [InlineData("?desde=2025-01-01&hasta=2026-10-31", "El período puede abarcar como máximo 366 días.")]
    [InlineData("?rango=siempre", "El rango indicado no existe.")]
    [InlineData("?estado=perdida", "El estado indicado no existe.")]
    [InlineData("?tecnico=999", "El técnico indicado no existe.")]
    public async Task Invalid_filters_are_explained_with_status_400(string query, string message)
    {
        using var client = await ClientAsync();

        using var response = await client.GetAsync("/reportes" + query, Token);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Revisa el siguiente filtro", html);
        Assert.Contains(message, html);
        Assert.DoesNotContain("Indicadores del período", html);
    }

    [Fact]
    public async Task An_empty_period_says_so_without_invented_numbers()
    {
        using var client = await ClientAsync();

        var html = await PageAsync(client, "/reportes?desde=2026-03-01&hasta=2026-03-31");

        Assert.Contains("Sin actividad en el período", html);
        Assert.Equal(["0", "0", "0", "0 de 0", "Sin datos", "Sin datos", "Sin datos", "Sin datos"], MetricValues(html));
        Assert.DoesNotContain("class=\"chart chart--ancho\"", html);
    }

    [Fact]
    public async Task Filters_narrow_every_indicator()
    {
        using var client = await ClientAsync();

        var unassigned = await PageAsync(client, October + "&tecnico=sin-asignar");
        var normal = await PageAsync(client, October + "&prioridad=normal");

        Assert.Equal(["1", "0", "0"], MetricValues(unassigned)[..3]);
        Assert.Equal(["2", "3", "1"], MetricValues(normal)[..3]);
        Assert.Contains("Filtros: prioridad Normal", normal);
    }

    [Fact]
    public async Task The_read_only_account_sees_the_report_but_cannot_export()
    {
        using var client = await ClientAsync(TestAccounts.ReadOnly);

        var html = await PageAsync(client, October);
        using var export = await client.GetAsync(OctoberExport + "&por=entrega", Token);

        Assert.Contains("Cuenta de solo consulta", html);
        Assert.Contains("La exportación CSV está disponible para las cuentas de Administrador y Operador.", html);
        Assert.DoesNotContain("Exportar CSV", html);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        Assert.DoesNotContain("text/csv", export.Content.Headers.ContentType?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task The_operator_can_export()
    {
        using var client = await ClientAsync(TestAccounts.Operator);

        using var export = await client.GetAsync(OctoberExport + "&por=entrega", Token);

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Deliveries_are_exported_as_utf8_csv_without_contact_data_or_caching()
    {
        using var client = await ClientAsync();

        using var response = await client.GetAsync(OctoberExport + "&por=entrega", Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(Token);
        var text = Encoding.UTF8.GetString(bytes.AsSpan(3));
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("fixtrack-ordenes-entregadas-2026-10-01-a-2026-10-31.csv", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("Orden,Fecha de recepción (hora local),Estado", lines[0], StringComparison.Ordinal);
        Assert.Equal(["Atasco de papel", "No enciende", "Escáner", "Teclado"], lines[1..].Select(line => line.Split(',')[10]));
        Assert.Contains("2026-10-04 12:00,400.00,450.00", lines[2], StringComparison.Ordinal);
        foreach (var secret in new[] { "5555-0101", "5555-0201", "ana1@fixtrack.test", "carla1@fixtrack.test", "Contraseña", "Password", "SecurityStamp" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task An_export_over_the_limit_is_not_generated_and_the_user_is_told()
    {
        using var client = await ClientAsync();

        using var response = await client.GetAsync(OctoberExport + "&por=recepcion", Token);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("/reportes", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains($"La exportación supera el máximo de {PortfolioExtrasFactory.ExportMaxRows} órdenes y no se generó.", html);
        Assert.Contains("1 oct 2026 – 31 oct 2026", html);
    }

    [Fact]
    public async Task An_export_with_invalid_filters_goes_back_to_the_report()
    {
        using var client = await ClientAsync();

        using var response = await client.GetAsync("/reportes/exportar?desde=2026-10-31&hasta=2026-10-01&por=entrega", Token);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token));
        using var badBasis = await client.GetAsync(OctoberExport + "&por=siempre", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("No se pudo exportar: revisa el período y los filtros.", html);
        Assert.Contains("La fecha final debe ser igual o posterior a la inicial.", html);
        Assert.Equal("text/html", badBasis.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Formulas_written_in_an_order_are_exported_as_text()
    {
        using var client = await ClientAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
            var device = await db.Devices.Include(item => item.Customer).FirstAsync(item => item.SerialNumber == "RPT-DELL-1", Token);
            var order = new ServiceOrder(
                device, "=HYPERLINK(\"http://ejemplo.invalid\",\"Ver\"), con \"comillas\"\ny salto", ServiceOrderPriority.Normal,
                new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc), null, null, null);

            // Cancelada: no cambia las órdenes abiertas que comprueban las demás pruebas de esta clase.
            order.ChangeStatus(ServiceOrderStatus.Cancelled, new DateTime(2026, 11, 2, 16, 0, 0, DateTimeKind.Utc), "Prueba de exportación");
            db.ServiceOrders.Add(order);
            await db.SaveChangesAsync(Token);
        }

        var csv = Encoding.UTF8.GetString(await client.GetByteArrayAsync("/reportes/exportar?desde=2026-11-02&hasta=2026-11-02&por=recepcion", Token));

        Assert.Contains(",\"'=HYPERLINK(\"\"http://ejemplo.invalid\"\",\"\"Ver\"\"), con \"\"comillas\"\"\ny salto\",", csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);
    }

    [GeneratedRegex("<dd class=\"metric__value[^\"]*\">(?<value>.*?)</dd>", RegexOptions.Singleline)]
    private static partial Regex MetricValue();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex("<tr>\\s*<th scope=\"row\">\\s*(?<name>[^<]+?)\\s*(<span|</th>)", RegexOptions.Singleline)]
    private static partial Regex TechnicianRow();
}
