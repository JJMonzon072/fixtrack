using System.Reflection;
using FixTrack.Web.Portfolio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>Los extras de portafolio están desactivados por defecto y un extra desactivado no registra su controlador.</summary>
public sealed class PortfolioFeaturesTests(FixTrackWebApplicationFactory factory) : IClassFixture<FixTrackWebApplicationFactory>
{
    [Fact]
    public void Every_extra_is_disabled_by_default()
    {
        var features = factory.Services.GetRequiredService<IOptions<PortfolioFeatures>>().Value;

        Assert.False(features.Reports);
        Assert.False(features.PrintableSheets);
        Assert.False(features.ReadOnlyApi);
        Assert.Empty(features.EnabledNames());
        Assert.Equal(PortfolioFeatures.DefaultExportMaxRows, features.ExportMaxRows);
    }

    [Theory]
    [InlineData(false, false, false, new string[0])]
    [InlineData(true, false, false, new[] { nameof(ReportsLike) })]
    [InlineData(false, true, false, new[] { nameof(SheetsLike) })]
    [InlineData(false, false, true, new[] { nameof(ApiLike) })]
    [InlineData(true, true, true, new[] { nameof(ReportsLike), nameof(SheetsLike), nameof(ApiLike) })]
    public void Each_flag_keeps_only_its_own_controllers(bool reports, bool sheets, bool api, string[] expectedExtras)
    {
        var application = new ApplicationModel();
        foreach (var type in new[] { typeof(AcademicLike), typeof(ReportsLike), typeof(SheetsLike), typeof(ApiLike) })
        {
            application.Controllers.Add(new ControllerModel(type.GetTypeInfo(), type.GetCustomAttributes(inherit: false)) { ControllerName = type.Name });
        }

        new PortfolioFeatureConvention(new PortfolioFeatures { Reports = reports, PrintableSheets = sheets, ReadOnlyApi = api }).Apply(application);

        Assert.Equal([nameof(AcademicLike), .. expectedExtras], application.Controllers.Select(controller => controller.ControllerName));
    }

    [Fact]
    public void The_convention_is_registered_in_mvc()
    {
        var options = factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value;

        Assert.Contains(options.Conventions, convention => convention is PortfolioFeatureConvention);
    }

    private sealed class AcademicLike : Controller;

    [PortfolioFeature(PortfolioFeature.Reports)]
    private sealed class ReportsLike : Controller;

    [PortfolioFeature(PortfolioFeature.PrintableSheets)]
    private sealed class SheetsLike : Controller;

    [PortfolioFeature(PortfolioFeature.ReadOnlyApi)]
    private sealed class ApiLike : Controller;
}
