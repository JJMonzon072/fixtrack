namespace FixTrack.Tests.Web.Portfolio;

/// <summary>FixTrack en memoria con los tres extras de portafolio habilitados (solo en las pruebas).</summary>
public sealed class PortfolioExtrasFactory : FixTrackWebApplicationFactory
{
    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings =>
    [
        new("PortfolioFeatures:Reports", "true"),
        new("PortfolioFeatures:PrintableSheets", "true"),
        new("PortfolioFeatures:ReadOnlyApi", "true"),
    ];
}
