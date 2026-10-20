using System.Net;
using FixTrack.Domain.ServiceOrders;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FixTrack.Tests.Web;

/// <summary>Página de auditoría (solo administradores) y actividad de la orden en su línea de tiempo.</summary>
public sealed class AuditPageTests(FixTrackWebApplicationFactory factory) : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<int> CreateOrderAsync(HttpClient client, string tag)
    {
        var customerId = await FormPost.CreateCustomerAsync(client, $"Cliente {tag}", "5555-7100");
        var deviceId = FormPost.CreatedId(await FormPost.SendAsync(client, "/equipos/nuevo", new()
        {
            ["CustomerId"] = customerId.ToString(),
            ["Type"] = "1",
            ["Brand"] = "Lenovo",
            ["Model"] = "ThinkPad",
            ["SerialNumber"] = $"SN-{tag}",
        }));
        return FormPost.CreatedId(await FormPost.SendAsync(client, "/ordenes/nueva", new()
        {
            ["CustomerId"] = customerId.ToString(),
            ["DeviceId"] = deviceId.ToString(),
            ["ReportedIssue"] = "Pantalla con líneas.",
            ["Priority"] = ((int)ServiceOrderPriority.Normal).ToString(),
            ["EstimatedCost"] = "300",
        }));
    }

    [Fact]
    public async Task Administrators_see_who_did_each_operation_and_can_filter_by_area()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateOrderAsync(admin, "auditoria");
        var number = ServiceOrder.FormatNumber(id);
        await FormPost.SendAsync(admin, $"/ordenes/{id}/estado", new()
        {
            ["Status"] = ((int)ServiceOrderStatus.Cancelled).ToString(),
            ["Note"] = "El cliente retiró el equipo sin reparar",
            ["Version"] = "0",
        });

        var all = await admin.GetStringAsync("/auditoria", Token);
        var orders = await admin.GetStringAsync("/auditoria?area=ordenes", Token);
        var accounts = await admin.GetStringAsync("/auditoria?area=cuentas", Token);

        Assert.Contains("Orden registrada", all);
        Assert.Contains("Orden cancelada", all);
        Assert.Contains($"{number}: Cancelada desde Recibida. Motivo: El cliente retiró el equipo sin reparar", all);
        Assert.Contains(TestAccounts.Administrator.FullName, all);
        Assert.Contains($"href=\"/ordenes/{id}\"", all);
        Assert.Contains("Orden cancelada", orders);
        Assert.DoesNotContain($"{number}:", accounts);
    }

    [Fact]
    public async Task Operators_cannot_open_the_audit_log_or_see_its_link()
    {
        using var operatorClient = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var forbidden = await operatorClient.GetAsync("/auditoria?area=cuentas", Token);
        var dashboard = await operatorClient.GetStringAsync("/", Token);
        using var redirected = await anonymous.GetAsync("/auditoria", Token);
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var adminDashboard = await admin.GetStringAsync("/", Token);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.DoesNotContain("href=\"/auditoria\"", dashboard);
        Assert.Equal(HttpStatusCode.Redirect, redirected.StatusCode);
        Assert.Contains("href=\"/auditoria\"", adminDashboard);
    }

    [Fact]
    public async Task Plan_changes_appear_in_the_order_timeline_with_their_author()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateOrderAsync(admin, "planificacion");

        using var response = await FormPost.SendAsync(admin, $"/ordenes/{id}/planificacion", new()
        {
            ["Priority"] = ((int)ServiceOrderPriority.Urgent).ToString(),
            ["PromisedAt"] = string.Empty,
            ["EstimatedCost"] = "450",
            ["Version"] = "0",
        });
        var details = await response.Content.ReadAsStringAsync(Token);

        Assert.Contains("Planificación actualizada", details);
        Assert.Contains("Prioridad Normal → Urgente; costo estimado Q 300.00 → Q 450.00.", details);
        Assert.Contains($"por {TestAccounts.Administrator.FullName}", details);
    }
}
