using System.Net;
using System.Text.RegularExpressions;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>
/// Fichas imprimibles con la aplicación real. Las órdenes de <c>ReportSeed</c> se insertan en orden: A=1, B=2, C=3, D=4,
/// E=5, F=6 (cancelada), G=7, H=8, I=9 (en reparación), J=10, K=11.
/// </summary>
public sealed partial class ServiceSheetsTests(PortfolioExtrasFactory factory) : IClassFixture<PortfolioExtrasFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(TestAccount? account = null)
    {
        await factory.SeedReportsAsync();
        return await factory.CreateAuthenticatedClientAsync(account);
    }

    private static async Task<(HttpStatusCode Status, string Html)> GetAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Token);
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Token)));
    }

    [Fact]
    public async Task The_reception_sheet_has_every_intake_detail_and_space_to_sign()
    {
        using var client = await ClientAsync();

        var (status, html) = await GetAsync(client, "/fichas/recepcion/1");

        Assert.Equal(HttpStatusCode.OK, status);
        foreach (var text in new[]
        {
            "<h1 class=\"sheet__doc-type\">Ficha de recepción</h1>", "<p class=\"sheet__number\">OS-000001</p>",
            "Emitido el <time datetime=\"2026-11-03T18:00:00.0000000Z\">3 de noviembre de 2026, 12:00</time>", "1 de octubre de 2026, 00:00", "<dd>Alta</dd>",
            "5 de octubre de 2026, 12:00", "Q 400.00", "Ana Morales", "5555-0101", "ana1@fixtrack.test", "Laptop", "Dell",
            "Latitude 5440", "RPT-DELL-1", "No enciende", "Con cargador", "Entrega el equipo (cliente)", "Recibe el equipo (FixTrack)",
            "No es una factura ni un comprobante fiscal o tributario.",
        })
        {
            Assert.Contains(text, html);
        }
    }

    [Fact]
    public async Task Missing_optional_fields_are_named_instead_of_left_blank()
    {
        using var client = await ClientAsync();
        int id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
            var customer = await db.Customers.FirstAsync(item => item.FullName == "Ana Morales", Token);
            var device = customer.RegisterDevice(DeviceType.Other, "Genérico", null, null, null, new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc));
            await db.SaveChangesAsync(Token);
            await db.Entry(device).Reference(item => item.Customer).LoadAsync(Token);
            var order = new ServiceOrder(device, "Revisión general", ServiceOrderPriority.Low, new DateTime(2026, 10, 30, 15, 0, 0, DateTimeKind.Utc), null, null, null);
            db.ServiceOrders.Add(order);
            await db.SaveChangesAsync(Token);
            id = order.Id;
        }

        var (_, html) = await GetAsync(client, $"/fichas/recepcion/{id}");

        Assert.Contains("Sin fecha comprometida", html);
        Assert.Contains("Por definir después del diagnóstico", html);
        Assert.Contains("Sin accesorios ni observaciones registrados al recibir el equipo.", html);
        Assert.Equal(2, Regex.Count(html, "<dd( class=\"sheet__mono\")?>No registrado</dd>"));
        Assert.Contains("Otro equipo", html);
    }

    [Fact]
    public async Task The_delivery_receipt_shows_the_diagnosis_final_cost_and_technician()
    {
        using var client = await ClientAsync();

        var (status, html) = await GetAsync(client, "/fichas/entrega/1");
        var (_, withoutEstimate) = await GetAsync(client, "/fichas/entrega/2");

        Assert.Equal(HttpStatusCode.OK, status);
        foreach (var text in new[]
        {
            "<h1 class=\"sheet__doc-type\">Comprobante de entrega</h1>", "<dd>Entregada</dd>", "4 de octubre de 2026, 12:00",
            "Q 450.00", "Fuente dañada", "Carla Ruiz", "Laptops", "+Q 50.00", "Recibí conforme (cliente)",
            "No es una factura ni un comprobante fiscal o tributario.",
        })
        {
            Assert.Contains(text, html);
        }

        Assert.Contains("Sin estimación", withoutEstimate);
        Assert.Contains("Luis Pérez", withoutEstimate);
        Assert.DoesNotContain("Diferencia", withoutEstimate);
    }

    [Theory]
    [InlineData(9, "En reparación")]
    [InlineData(6, "Cancelada")]
    [InlineData(3, "Recibida")]
    public async Task There_is_no_delivery_receipt_for_an_order_that_was_not_delivered(int id, string status)
    {
        using var client = await ClientAsync();

        var (code, html) = await GetAsync(client, $"/fichas/entrega/{id}");

        Assert.Equal(HttpStatusCode.Conflict, code);
        Assert.Contains("El comprobante de entrega aún no está disponible", html);
        Assert.Contains($"está «{status}»", html);
        Assert.DoesNotContain("sheet__doc-type", html);
        Assert.Contains($"href=\"/fichas/recepcion/{id}\"", html);
    }

    [Theory]
    [InlineData("/fichas/recepcion/9999")]
    [InlineData("/fichas/entrega/9999")]
    [InlineData("/fichas/recepcion/abc")]
    public async Task Unknown_orders_answer_404(string url)
    {
        using var client = await ClientAsync();

        var (status, html) = await GetAsync(client, url);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("Página no encontrada", html);
        Assert.DoesNotContain("Ana Morales", html);
    }

    [Fact]
    public async Task Sheets_require_a_session_and_are_readable_by_every_workshop_role()
    {
        await factory.SeedReportsAsync();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var readOnly = await factory.CreateAuthenticatedClientAsync(TestAccounts.ReadOnly);
        using var worker = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);

        using var anonymousResponse = await anonymous.GetAsync("/fichas/recepcion/1", Token);
        var body = await anonymousResponse.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("http://localhost/cuenta/iniciar-sesion", anonymousResponse.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Ana Morales", body);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(readOnly, "/fichas/entrega/1")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(worker, "/fichas/recepcion/1")).Status);
    }

    [Fact]
    public async Task The_document_layout_has_no_app_navigation_inline_styles_or_unsigned_scripts()
    {
        using var client = await ClientAsync();

        var (_, html) = await GetAsync(client, "/fichas/recepcion/1");

        Assert.DoesNotContain("class=\"nav-link\"", html);
        Assert.DoesNotContain(" style=\"", html, StringComparison.Ordinal);
        Assert.Matches("<link rel=\"stylesheet\" href=\"/css/sheets\\.[a-z0-9]+\\.css\" />", html);
        Assert.Matches("<script src=\"/js/sheets\\.[a-z0-9]+\\.js\" defer></script>", html);
        Assert.Matches("<script nonce=\"[^\"]+\">", html);
        Assert.Contains("data-print", html);
        Assert.Contains("href=\"/ordenes/1\"", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\" />", html);
    }

    [Fact]
    public async Task Text_written_in_an_order_is_escaped()
    {
        using var client = await ClientAsync();
        int id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
            var device = await db.Devices.Include(item => item.Customer).FirstAsync(item => item.SerialNumber == "RPT-HP-1", Token);
            var order = new ServiceOrder(device, "<script>alert('x')</script> & \"comillas\"", ServiceOrderPriority.Normal,
                new DateTime(2026, 10, 30, 16, 0, 0, DateTimeKind.Utc), null, null, "<img src=x onerror=alert(1)>");
            db.ServiceOrders.Add(order);
            await db.SaveChangesAsync(Token);
            id = order.Id;
        }

        var raw = await client.GetStringAsync($"/fichas/recepcion/{id}", Token);

        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_order_detail_links_to_the_sheets_that_exist()
    {
        using var client = await ClientAsync();

        var (_, delivered) = await GetAsync(client, "/ordenes/1");
        var (_, inRepair) = await GetAsync(client, "/ordenes/9");

        Assert.Contains("href=\"/fichas/recepcion/1\"", delivered);
        Assert.Contains("href=\"/fichas/entrega/1\"", delivered);
        Assert.Contains("aria-label=\"Documentos de la orden\"", delivered);
        Assert.Contains("href=\"/fichas/recepcion/9\"", inRepair);
        Assert.DoesNotContain("href=\"/fichas/entrega/9\"", inRepair);
    }

    [Fact]
    public async Task The_print_stylesheet_targets_a4_in_black_and_white_and_hides_the_toolbar()
    {
        using var client = await ClientAsync();

        var css = await client.GetStringAsync("/css/sheets.css", Token);

        Assert.Contains("size: A4;", css);
        Assert.Contains("@page {", css);
        Assert.Contains("counter(page)", css);
        Assert.Contains("@media print {", css);
        Assert.Matches(@"@media print \{[\s\S]*\.sheet-toolbar \{\s*display: none;", css);
        Assert.Contains("print-color-adjust: economy;", css);
        Assert.Contains("break-inside: avoid;", css);
        Assert.Contains("white-space: pre-wrap;", css);
        Assert.Contains("overflow-wrap: anywhere;", css);
    }
}
