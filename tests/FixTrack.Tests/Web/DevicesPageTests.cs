using System.Net;
using System.Text.Json;
using FixTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Módulo Equipos e integración con Clientes, de punta a punta sobre SQLite en memoria.</summary>
public sealed class DevicesPageTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> DeviceForm(int customerId, string brand, string? serial = null, string type = "1") => new()
    {
        ["CustomerId"] = customerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Type"] = type,
        ["Brand"] = brand,
        ["Model"] = "Modelo de prueba",
        ["SerialNumber"] = serial ?? string.Empty,
        ["Notes"] = string.Empty,
    };

    [Fact]
    public async Task Shows_validation_errors_including_a_missing_owner()
    {
        using var client = factory.CreateClient();

        var response = await FormPost.SendAsync(client, "/equipos/nuevo", new()
        {
            ["CustomerId"] = string.Empty,
            ["Type"] = string.Empty,
            ["Brand"] = "  ",
            ["SerialNumber"] = "ÑÑÑ",
        });
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.EndsWith("/equipos/nuevo", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Revisa los siguientes 4 campos", html);
        Assert.Contains("Selecciona el cliente propietario del equipo.", html);
        Assert.Contains("Selecciona el tipo de equipo.", html);
        Assert.Contains("La marca es obligatoria.", html);
        Assert.Contains("El número de serie solo admite", html);
    }

    [Fact]
    public async Task Rejects_a_duplicate_serial_number_within_the_same_brand()
    {
        using var client = factory.CreateClient();
        var customerId = await FormPost.CreateCustomerAsync(client, "Diego Serie", "5555-2004");
        await FormPost.SendAsync(client, "/equipos/nuevo", DeviceForm(customerId, "HP", "HP-DUP-01"));

        var duplicate = await (await FormPost.SendAsync(client, "/equipos/nuevo", DeviceForm(customerId, "HP", "hp-dup-01"))).Content.ReadAsStringAsync(Token);
        var otherBrand = await FormPost.SendAsync(client, "/equipos/nuevo", DeviceForm(customerId, "Epson", "HP-DUP-01"));

        Assert.Contains("Ya existe un equipo de esta marca con el mismo número de serie.", duplicate);
        Assert.Matches(@"/equipos/\d+$", otherBrand.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Edits_a_device_without_changing_its_owner_even_if_the_form_is_tampered_with()
    {
        using var client = factory.CreateClient();
        var ownerId = await FormPost.CreateCustomerAsync(client, "Elena Duena", "5555-2005");
        var otherId = await FormPost.CreateCustomerAsync(client, "Felipe Otro", "5555-2006");
        var deviceId = FormPost.CreatedId(await FormPost.SendAsync(client, "/equipos/nuevo", DeviceForm(ownerId, "Acer")));

        var edit = DeviceForm(otherId, "Acer", "AC-777", type: "2");
        edit["Model"] = "Aspire TC";
        edit["Notes"] = "Fuente reemplazada";
        var html = await (await FormPost.SendAsync(client, $"/equipos/{deviceId}/editar", edit)).Content.ReadAsStringAsync(Token);

        Assert.Contains("Los cambios se guardaron correctamente.", html);
        Assert.Contains(">Acer Aspire TC</h1>", html);
        Assert.Contains("Computadora de escritorio", html);
        Assert.Contains("Fuente reemplazada", html);
        await using var scope = factory.Services.CreateAsyncScope();
        var device = await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Devices.SingleAsync(d => d.Id == deviceId, Token);
        Assert.Equal(ownerId, device.CustomerId);
    }

    [Fact]
    public async Task Searches_and_filters_devices()
    {
        using var client = factory.CreateClient();
        var customerId = await FormPost.CreateCustomerAsync(client, "Gabriela Filtro", "5555-2007");
        await FormPost.SendAsync(client, "/equipos/nuevo", DeviceForm(customerId, "Brother", "BR-FILTRO", type: "5"));

        var bySerial = await client.GetStringAsync("/equipos?buscar=br-filtro", Token);
        var byCustomer = await client.GetStringAsync("/equipos?buscar=gabriela", Token);
        var byType = await client.GetStringAsync("/equipos?buscar=br-filtro&tipo=impresora", Token);
        var wrongType = await client.GetStringAsync("/equipos?buscar=br-filtro&tipo=laptop", Token);

        Assert.Contains(">Brother Modelo de prueba</a>", bySerial);
        Assert.Contains(">Brother Modelo de prueba</a>", byCustomer);
        Assert.Contains(">Brother Modelo de prueba</a>", byType);
        Assert.Contains("No hay equipos que coincidan", wrongType);
    }

    [Fact]
    public async Task Suggests_only_active_customers_as_json()
    {
        using var client = factory.CreateClient();
        await FormPost.CreateCustomerAsync(client, "Hector Sugerido", "5555-2008");
        var inactiveId = await FormPost.CreateCustomerAsync(client, "Hector Inactivo", "5555-2009");
        await FormPost.SendAsync(client, $"/clientes/{inactiveId}/desactivar", []);

        using var response = await client.GetAsync("/clientes/opciones?buscar=hector", Token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var names = json.RootElement.EnumerateArray().Select(option => option.GetProperty("fullName").GetString()).ToList();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["Hector Sugerido"], names);
    }

    [Fact]
    public async Task Rejects_posts_without_an_antiforgery_token()
    {
        using var client = factory.CreateClient();

        using var create = await client.PostAsync("/equipos/nuevo", new FormUrlEncodedContent(new Dictionary<string, string> { ["Brand"] = "Dell" }), Token);
        using var edit = await client.PostAsync("/equipos/1/editar", new FormUrlEncodedContent(new Dictionary<string, string> { ["Brand"] = "Dell" }), Token);

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
    }

    [Theory]
    [InlineData("/equipos/999999")]
    [InlineData("/equipos/999999/editar")]
    public async Task Unknown_devices_return_not_found(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
