using FixTrack.Application;
using FixTrack.Application.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Application;

public sealed class ModuleCatalogTests
{
    private readonly IModuleCatalog _catalog = new ServiceCollection()
        .AddApplication()
        .BuildServiceProvider()
        .GetRequiredService<IModuleCatalog>();

    [Fact]
    public void Catalog_contains_every_module_exactly_once()
    {
        var keys = _catalog.Modules.Select(module => module.Key).ToList();

        Assert.Equal(Enum.GetValues<ModuleKey>().Order(), keys.Order());
    }

    [Theory]
    [InlineData(ModuleKey.Dashboard, "Panel")]
    [InlineData(ModuleKey.ServiceOrders, "Órdenes de servicio")]
    [InlineData(ModuleKey.Customers, "Clientes")]
    [InlineData(ModuleKey.Devices, "Equipos")]
    [InlineData(ModuleKey.Technicians, "Técnicos")]
    public void Get_returns_the_requested_module(ModuleKey key, string expectedName)
    {
        var module = _catalog.Get(key);

        Assert.Equal(key, module.Key);
        Assert.Equal(expectedName, module.Name);
        Assert.False(string.IsNullOrWhiteSpace(module.Description));
    }

    [Fact]
    public void Every_module_is_available_since_phase_3()
    {
        Assert.All(_catalog.Modules, module => Assert.True(module.IsAvailable, $"{module.Name} sigue marcado como planificado."));
        Assert.All(_catalog.Modules, module => Assert.Empty(module.PlannedFeatures));
    }

    [Fact]
    public void Get_rejects_an_unknown_module()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.Get((ModuleKey)999));
    }
}
