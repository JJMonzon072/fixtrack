using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>
/// Rol Consulta: abre todas las pantallas (incluidas usuarios y auditoría) para demostraciones y para el manual, pero no
/// puede guardar nada, ni siquiera enviando los formularios a mano.
/// </summary>
public sealed partial class ReadOnlyRoleTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private const string Banner = "Cuenta de solo consulta: puedes ver todas las pantallas, pero no guardar cambios.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Un registro de cada tipo, creado por el administrador, para las rutas con Id.</summary>
    private sealed record Scenario(int Customer, int Device, int Technician, int Order, string Account);

    private async Task<Scenario> ScenarioAsync()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var customer = await FormPost.CreateCustomerAsync(admin, $"Cliente Consulta {tag}", "5555-7100");
        var device = FormPost.CreatedId(await FormPost.SendAsync(admin, "/equipos/nuevo", new()
        {
            ["CustomerId"] = customer.ToString(CultureInfo.InvariantCulture),
            ["Type"] = "2",
            ["Brand"] = "Dell",
            ["Model"] = "OptiPlex",
            ["SerialNumber"] = $"RO-{tag}",
        }));
        var technician = FormPost.CreatedId(await FormPost.SendAsync(admin, "/tecnicos/nuevo", new()
        {
            ["FullName"] = $"Tecnico Consulta {tag}",
            ["Specialty"] = "Redes",
        }));
        var order = FormPost.CreatedId(await FormPost.SendAsync(admin, "/ordenes/nueva", new()
        {
            ["CustomerId"] = customer.ToString(CultureInfo.InvariantCulture),
            ["DeviceId"] = device.ToString(CultureInfo.InvariantCulture),
            ["ReportedIssue"] = "No enciende.",
            ["Priority"] = "2",
            ["TechnicianId"] = technician.ToString(CultureInfo.InvariantCulture),
            ["PromisedAt"] = string.Empty,
            ["EstimatedCost"] = string.Empty,
        }));
        var account = (await AccountsAsync()).Single(item => item.Email == TestAccounts.ReadOnly.Email).Id;
        return new Scenario(customer, device, technician, order, account);
    }

    /// <summary>Rutas de la aplicación con el método indicado que exigen sesión, con sus Id sustituidos.</summary>
    private List<string> Routes(string method, Scenario scenario) =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(endpoint => endpoint.RoutePattern.RawText is { } raw && !raw.Contains("{**", StringComparison.Ordinal))
            .Select(endpoint => "/" + RouteId().Replace(endpoint.RoutePattern.RawText!, match => match.Groups["type"].Value switch
            {
                "guid" => scenario.Account,
                _ => (endpoint.RoutePattern.RawText!.Split('/')[0] switch
                {
                    "clientes" => scenario.Customer,
                    "equipos" => scenario.Device,
                    "tecnicos" => scenario.Technician,
                    _ => scenario.Order,
                }).ToString(CultureInfo.InvariantCulture),
            }))
            .Distinct()
            .Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Opens_every_page_including_users_audit_and_the_forms()
    {
        var scenario = await ScenarioAsync();
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.ReadOnly);
        // "acceso-denegado" responde 403 a todos: es el destino de la redirección por falta de permisos.
        var urls = Routes(HttpMethods.Get, scenario).Where(url => url != "/cuenta/acceso-denegado").ToList();

        var failures = new List<string>();
        foreach (var url in urls)
        {
            using var response = await client.GetAsync(url, Token);
            var body = await response.Content.ReadAsStringAsync(Token);
            var isPage = response.Content.Headers.ContentType?.MediaType == "text/html";
            if (response.StatusCode != HttpStatusCode.OK || (isPage && !body.Contains(Banner, StringComparison.Ordinal)))
            {
                failures.Add($"{url} respondió {(int)response.StatusCode}{(isPage ? string.Empty : " (sin HTML)")}");
            }
        }

        Assert.True(urls.Count >= 20, string.Join(", ", urls));
        Assert.Contains("/usuarios", urls);
        Assert.Contains("/auditoria", urls);
        Assert.Contains("/tecnicos/nuevo", urls);
        Assert.Contains($"/ordenes/{scenario.Order}/estado", urls);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task The_menu_offers_users_and_hides_the_password_change()
    {
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.ReadOnly);

        var html = await client.GetStringAsync("/", Token);

        Assert.Contains("Usuarios y roles", html);
        Assert.Contains(">Consulta</span>", html);
        Assert.DoesNotContain("Cambiar contraseña", html);
        Assert.Contains("Cerrar sesión", html);
    }

    /// <summary>
    /// Recorre todos los POST con sesión de la aplicación (los actuales y los que se agreguen), salvo cerrar sesión, y los
    /// envía con la sesión y el token antifalsificación válidos de la cuenta de consulta. El Id de cuenta de la ruta es el
    /// suyo (el caso peligroso: ascenderse a Administrador).
    /// </summary>
    [Fact]
    public async Task Gets_403_on_every_post_and_nothing_changes()
    {
        var scenario = await ScenarioAsync();
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.ReadOnly);
        var urls = Routes(HttpMethods.Post, scenario).Where(url => url != "/cuenta/cerrar-sesion").ToList();
        var before = await SnapshotAsync();

        var failures = new List<string>();
        foreach (var url in urls)
        {
            using var response = await FormPost.SendAsync(client, url, new()
            {
                ["FullName"] = "Consulta Ascendida",
                ["Email"] = $"ascenso-{Guid.NewGuid():N}@fixtrack.test",
                ["Phone"] = "5555-0000",
                ["Role"] = Roles.Administrator,
                ["Specialty"] = "Intrusión",
                ["CustomerId"] = scenario.Customer.ToString(CultureInfo.InvariantCulture),
                ["DeviceId"] = scenario.Device.ToString(CultureInfo.InvariantCulture),
                ["TechnicianId"] = scenario.Technician.ToString(CultureInfo.InvariantCulture),
                ["ReportedIssue"] = "Intento de escritura.",
                ["Priority"] = "3",
                ["CurrentPassword"] = TestAccounts.ReadOnly.Password,
                ["Password"] = "Ascenso1234567",
                ["ConfirmPassword"] = "Ascenso1234567",
            });
            var body = await response.Content.ReadAsStringAsync(Token);
            if (response.StatusCode != HttpStatusCode.Forbidden || !body.Contains("Esta cuenta es de solo consulta", StringComparison.Ordinal))
            {
                failures.Add($"{url} respondió {(int)response.StatusCode}");
            }
        }

        // Clientes, equipos, técnicos, órdenes (estado, asignación, diagnóstico, planificación), cuentas y contraseña propia.
        Assert.True(urls.Count >= 20, string.Join(", ", urls));
        Assert.Contains($"/usuarios/{scenario.Account}/editar", urls);
        Assert.Contains("/cuenta/contrasena", urls);
        Assert.Contains("/ordenes/nueva", urls);
        Assert.Empty(failures);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Can_log_out()
    {
        using var client = factory.CreateClient();
        using var login = await AuthenticatedClients.LogInAsync(client, TestAccounts.ReadOnly.Email, TestAccounts.ReadOnly.Password);
        Assert.Contains(Banner, await login.Content.ReadAsStringAsync(Token));

        using var logout = await FormPost.SendAsync(client, "/cuenta/cerrar-sesion", []);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal("/cuenta/iniciar-sesion", logout.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Other_roles_do_not_see_the_banner()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        using var worker = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);

        Assert.DoesNotContain(Banner, await admin.GetStringAsync("/", Token));
        Assert.DoesNotContain(Banner, await worker.GetStringAsync("/", Token));
    }

    [Fact]
    public void Only_get_and_head_requests_are_read_only()
    {
        foreach (var (method, expected) in new[] { ("GET", true), ("HEAD", true), ("POST", false), ("PUT", false), ("DELETE", false), ("PATCH", false) })
        {
            var http = new DefaultHttpContext();
            http.Request.Method = method;
            Assert.Equal(expected, RoleAccessHandler.ReadOnlyAllows(http));
        }

        // Sin recurso, una vista decide si muestra un enlace; un recurso desconocido se rechaza.
        Assert.True(RoleAccessHandler.ReadOnlyAllows(null));
        Assert.False(RoleAccessHandler.ReadOnlyAllows(new object()));
    }

    /// <summary>La página de error de un envío rechazado conserva el menú completo de la cuenta de consulta.</summary>
    [Fact]
    public async Task The_403_page_keeps_the_administration_links()
    {
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.ReadOnly);

        using var response = await FormPost.SendAsync(client, "/clientes/nuevo", new() { ["FullName"] = "Intento", ["Phone"] = "5555-1111" });
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("href=\"/usuarios\"", html);
        Assert.Contains("href=\"/auditoria\"", html);
    }

    private async Task<List<UserAccount>> AccountsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return [.. await scope.ServiceProvider.GetRequiredService<IUserAccountService>().ListAsync(Token)];
    }

    /// <summary>Filas de cada tabla y el estado de todas las cuentas, incluidos sus hashes y sellos.</summary>
    private async Task<string> SnapshotAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
        var users = await db.Users.AsNoTracking()
            .OrderBy(user => user.Id)
            .Select(user => $"{user.Id}|{user.FullName}|{user.PasswordHash}|{user.SecurityStamp}|{user.LockoutEnd}")
            .ToListAsync(Token);
        var roles = await db.UserRoles.AsNoTracking().OrderBy(link => link.UserId).Select(link => $"{link.UserId}:{link.RoleId}").ToListAsync(Token);
        // Cada edición incrementa la versión del registro: sumarlas detecta cambios, no solo altas.
        return string.Join(
            "\n",
            $"clientes {await db.Customers.CountAsync(Token)}/{await db.Customers.SumAsync(item => item.Version, Token)}",
            $"equipos {await db.Devices.CountAsync(Token)}/{await db.Devices.SumAsync(item => item.Version, Token)}",
            $"técnicos {await db.Technicians.CountAsync(Token)}/{await db.Technicians.SumAsync(item => item.Version, Token)}",
            $"órdenes {await db.ServiceOrders.CountAsync(Token)}/{await db.ServiceOrders.SumAsync(item => item.Version, Token)}",
            $"historial {await db.ServiceOrderStatusHistory.CountAsync(Token)} · asignaciones {await db.ServiceOrderAssignments.CountAsync(Token)}",
            $"auditoría {await db.AuditEvents.CountAsync(Token)}",
            string.Join("\n", users),
            string.Join("\n", roles));
    }

    [GeneratedRegex(@"\{id:(?<type>guid|int)\}")]
    private static partial Regex RouteId();
}
