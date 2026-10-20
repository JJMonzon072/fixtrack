using System.Net;
using System.Text.RegularExpressions;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Módulo Clientes de punta a punta (HTTP, MVC, servicio y EF Core sobre SQLite en memoria).</summary>
public sealed class CustomersPageTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registers_a_customer_and_shows_its_details()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await FormPost.SendAsync(client, "/clientes/nuevo", new()
        {
            ["FullName"] = "  Ana   Registro ",
            ["Phone"] = "+502 5555 1001",
            ["Email"] = "Ana.Registro@Example.com",
            ["Notes"] = "Prefiere WhatsApp",
        });
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(@"/clientes/\d+$", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Cliente registrado correctamente.", html);
        Assert.Contains(">Ana Registro</h1>", html);
        Assert.Contains("ana.registro@example.com", html);
        Assert.Contains("Prefiere WhatsApp", html);
    }

    [Fact]
    public async Task Rejects_posts_without_an_antiforgery_token()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.PostAsync(
            "/clientes/nuevo",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["FullName"] = "Sin Token", ["Phone"] = "5555-0000" }),
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await CustomerExistsAsync("Sin Token"));
    }

    [Fact]
    public async Task Shows_server_side_validation_errors_and_keeps_the_input()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await FormPost.SendAsync(client, "/clientes/nuevo", new()
        {
            ["FullName"] = "A",
            ["Phone"] = "12",
            ["Email"] = "no-es-correo",
        });
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith("/clientes/nuevo", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Revisa los siguientes 3 campos", html);
        Assert.Contains("El nombre debe tener entre 3 y 150 caracteres.", html);
        Assert.Contains("El teléfono debe tener entre 7 y 15 dígitos.", html);
        Assert.Contains("Ingresa un correo electrónico válido", html);
        Assert.Contains("value=\"no-es-correo\"", html);
        Assert.Equal(3, Regex.Count(html, "aria-invalid=\"true\""));
    }

    [Fact]
    public async Task Rejects_a_duplicate_email()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        await CreateCustomerAsync(client, "Carla Duplicado", "5555-1002", "carla.dup@example.com");

        var response = await FormPost.SendAsync(client, "/clientes/nuevo", new()
        {
            ["FullName"] = "Otra Carla",
            ["Phone"] = "5555-1003",
            ["Email"] = "CARLA.DUP@example.com",
        });
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Contains("Ya existe un cliente registrado con este correo electrónico.", html);
        Assert.False(await CustomerExistsAsync("Otra Carla"));
    }

    [Fact]
    public async Task Edits_a_customer()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateCustomerAsync(client, "Luis Edición", "5555-1004", null);

        var response = await FormPost.SendAsync(client, $"/clientes/{id}/editar", new()
        {
            ["FullName"] = "Luis Alberto Edición",
            ["Phone"] = "5555-1005",
            ["Email"] = "luis.edicion@example.com",
        });
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Contains("Los cambios se guardaron correctamente.", html);
        Assert.Contains(">Luis Alberto Edición</h1>", html);
        Assert.Contains("5555-1005", html);
    }

    [Fact]
    public async Task Deactivates_and_reactivates_a_customer_without_deleting_it()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateCustomerAsync(client, "Marta Estado", "5555-1006", null);

        var deactivated = await (await FormPost.SendAsync(client, $"/clientes/{id}/desactivar", [])).Content.ReadAsStringAsync(Token);
        Assert.Contains("Cliente desactivado.", deactivated);
        Assert.Contains("Este cliente está inactivo desde", deactivated);

        var activeList = await client.GetStringAsync("/clientes?buscar=Marta%20Estado", Token);
        var inactiveList = await client.GetStringAsync("/clientes?buscar=Marta%20Estado&estado=inactivos", Token);
        Assert.DoesNotContain(">Marta Estado</a>", activeList);
        Assert.Contains(">Marta Estado</a>", inactiveList);

        var reactivated = await (await FormPost.SendAsync(client, $"/clientes/{id}/reactivar", [])).Content.ReadAsStringAsync(Token);
        Assert.Contains("Cliente reactivado.", reactivated);
        Assert.True(await CustomerExistsAsync("Marta Estado"));
    }

    [Fact]
    public async Task Searches_customers_and_explains_empty_results()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        await CreateCustomerAsync(client, "Sofía Búsqueda", "5555-1007", "sofia.busqueda@example.com");

        var found = await client.GetStringAsync("/clientes?buscar=sofia.busqueda", Token);
        var missing = await client.GetStringAsync("/clientes?buscar=no-existe-nadie", Token);

        Assert.Contains(">Sofía Búsqueda</a>", found);
        Assert.Contains("No hay resultados para «no-existe-nadie»", missing);
        Assert.Contains("Limpiar búsqueda", missing);
    }

    [Fact]
    public async Task A_stale_edit_form_does_not_overwrite_a_newer_change()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = await FormPost.CreateCustomerAsync(client, "Cliente Concurrente", "5555-7300");
        var page = await client.GetStringAsync($"/clientes/{id}/editar", Token);
        var version = System.Text.RegularExpressions.Regex.Match(page, "name=\"Version\" value=\"(?<v>\\d+)\"").Groups["v"].Value;

        // Primera pestaña: guarda. Segunda pestaña: envía el formulario que abrió antes.
        await FormPost.SendAsync(client, $"/clientes/{id}/editar", new()
        {
            ["FullName"] = "Cliente Actualizado",
            ["Phone"] = "5555-7300",
            ["Version"] = version,
        });
        var stale = await (await FormPost.SendAsync(client, $"/clientes/{id}/editar", new()
        {
            ["FullName"] = "Cliente Desactualizado",
            ["Phone"] = "5555-0000",
            ["Version"] = version,
        })).Content.ReadAsStringAsync(Token);

        Assert.Equal("0", version);
        Assert.Contains("Otra persona modificó este cliente mientras lo editabas", stale);
        Assert.Contains("value=\"Cliente Actualizado\"", stale);
        var details = await client.GetStringAsync($"/clientes/{id}", Token);
        Assert.Contains("Cliente Actualizado", details);
        Assert.DoesNotContain("Cliente Desactualizado", details);
    }

    [Fact]
    public async Task User_input_is_html_encoded()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateCustomerAsync(client, "Pedro Seguro", "5555-1008", null, notes: "<script>alert(1)</script>");

        var html = await client.GetStringAsync($"/clientes/{id}", Token);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Theory]
    [InlineData("/clientes/999999")]
    [InlineData("/clientes/999999/editar")]
    public async Task Unknown_customers_return_not_found(string url)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<int> CreateCustomerAsync(HttpClient client, string name, string phone, string? email, string? notes = null) =>
        FormPost.CreatedId(await FormPost.SendAsync(client, "/clientes/nuevo", new()
        {
            ["FullName"] = name,
            ["Phone"] = phone,
            ["Email"] = email ?? string.Empty,
            ["Notes"] = notes ?? string.Empty,
        }));

    private async Task<bool> CustomerExistsAsync(string fullName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
        return await db.Customers.AnyAsync(c => c.FullName == fullName, Token);
    }
}
