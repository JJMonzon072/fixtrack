using System.Net;
using FixTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Módulo Técnicos de punta a punta sobre SQLite en memoria.</summary>
public sealed class TechniciansPageTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<int> CreateAsync(HttpClient client, string name, string? email = null) =>
        FormPost.CreatedId(await FormPost.SendAsync(client, "/tecnicos/nuevo", new()
        {
            ["FullName"] = name,
            ["Specialty"] = "Laptops y equipos de escritorio",
            ["Phone"] = "5555-3001",
            ["Email"] = email ?? string.Empty,
        }));

    [Fact]
    public async Task Registers_a_technician_and_shows_its_details()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await FormPost.SendAsync(client, "/tecnicos/nuevo", new()
        {
            ["FullName"] = " Ines   Registro ",
            ["Specialty"] = "Impresoras y periféricos",
            ["Phone"] = string.Empty,
            ["Email"] = "INES.REGISTRO@example.com",
        });
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Matches(@"/tecnicos/\d+$", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Técnico registrado correctamente.", html);
        Assert.Contains(">Ines Registro</h1>", html);
        Assert.Contains("Impresoras y periféricos", html);
        Assert.Contains("ines.registro@example.com", html);
        Assert.Contains("No tiene órdenes abiertas asignadas.", html);
    }

    [Fact]
    public async Task Shows_validation_errors()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var html = await (await FormPost.SendAsync(client, "/tecnicos/nuevo", new()
        {
            ["FullName"] = "",
            ["Specialty"] = "",
            ["Phone"] = "12",
            ["Email"] = "x@",
        })).Content.ReadAsStringAsync(Token);

        Assert.Contains("Revisa los siguientes 4 campos", html);
        Assert.Contains("El nombre es obligatorio.", html);
        Assert.Contains("La especialidad es obligatoria.", html);
        Assert.Contains("El teléfono debe tener entre 7 y 15 dígitos.", html);
        Assert.Contains("Ingresa un correo electrónico válido", html);
    }

    [Fact]
    public async Task Rejects_a_duplicate_email()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        await CreateAsync(client, "Jorge Correo", "jorge.correo@example.com");

        var html = await (await FormPost.SendAsync(client, "/tecnicos/nuevo", new()
        {
            ["FullName"] = "Otro Jorge",
            ["Specialty"] = "Redes",
            ["Email"] = "Jorge.Correo@example.com",
        })).Content.ReadAsStringAsync(Token);

        Assert.Contains("Ya existe un técnico registrado con este correo electrónico.", html);
    }

    [Fact]
    public async Task Edits_a_technician()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(client, "Karla Edicion");

        var html = await (await FormPost.SendAsync(client, $"/tecnicos/{id}/editar", new()
        {
            ["FullName"] = "Karla Edición",
            ["Specialty"] = "Redes y servidores",
            ["Phone"] = "5555-3002",
            ["Email"] = string.Empty,
        })).Content.ReadAsStringAsync(Token);

        Assert.Contains("Los cambios se guardaron correctamente.", html);
        Assert.Contains(">Karla Edición</h1>", html);
        Assert.Contains("Redes y servidores", html);
    }

    [Fact]
    public async Task Deactivates_and_reactivates_a_technician_without_deleting_it()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(client, "Leo Estado");

        var deactivated = await (await FormPost.SendAsync(client, $"/tecnicos/{id}/desactivar", [])).Content.ReadAsStringAsync(Token);
        var active = await client.GetStringAsync("/tecnicos?buscar=leo%20estado", Token);
        var inactive = await client.GetStringAsync("/tecnicos?buscar=leo%20estado&estado=inactivos", Token);
        var reactivated = await (await FormPost.SendAsync(client, $"/tecnicos/{id}/reactivar", [])).Content.ReadAsStringAsync(Token);

        Assert.Contains("Técnico desactivado.", deactivated);
        Assert.Contains("no podrá recibir nuevas asignaciones", deactivated);
        Assert.DoesNotContain(">Leo Estado</a>", active);
        Assert.Contains(">Leo Estado</a>", inactive);
        Assert.Contains("Técnico reactivado.", reactivated);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Technicians.AnyAsync(t => t.Id == id && t.IsActive, Token));
    }

    [Fact]
    public async Task The_status_cannot_be_changed_through_the_edit_form()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(client, "Mara Formulario");

        await FormPost.SendAsync(client, $"/tecnicos/{id}/editar", new()
        {
            ["FullName"] = "Mara Formulario",
            ["Specialty"] = "Laptops",
            ["IsActive"] = "false",
            ["DeactivatedAtUtc"] = "2020-01-01",
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var technician = await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Technicians.SingleAsync(t => t.Id == id, Token);
        Assert.True(technician.IsActive);
        Assert.Null(technician.DeactivatedAtUtc);
    }

    [Fact]
    public async Task Rejects_posts_without_an_antiforgery_token()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(client, "Nora Token");

        using var response = await client.PostAsync($"/tecnicos/{id}/desactivar", new FormUrlEncodedContent([]), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Technicians.AnyAsync(t => t.Id == id && t.IsActive, Token));
    }

    [Theory]
    [InlineData("/tecnicos/999999")]
    [InlineData("/tecnicos/999999/editar")]
    public async Task Unknown_technicians_return_not_found(string url)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
