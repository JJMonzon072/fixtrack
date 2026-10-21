namespace FixTrack.Web.Portfolio;

/// <summary>
/// Extras de portafolio, independientes del proyecto académico y desactivados por defecto (sección
/// <c>PortfolioFeatures</c> de la configuración). Con los tres en <c>false</c>, FixTrack se comporta exactamente como la
/// versión académica: sus rutas no existen, el menú no cambia y ninguna pantalla muestra enlaces nuevos.
/// Se leen al iniciar: cambiarlos exige reiniciar la aplicación. Ver docs/portfolio-extras.md.
/// </summary>
public sealed class PortfolioFeatures
{
    public const string SectionName = "PortfolioFeatures";

    /// <summary>Máximo predeterminado de filas de una exportación CSV.</summary>
    public const int DefaultExportMaxRows = 5000;

    /// <summary>Centro de reportes (<c>/reportes</c>) y su exportación CSV.</summary>
    public bool Reports { get; set; }

    /// <summary>Ficha de recepción y comprobante de entrega imprimibles (<c>/fichas/…</c>).</summary>
    public bool PrintableSheets { get; set; }

    /// <summary>API REST versionada de solo lectura (<c>/api/v1/…</c>).</summary>
    public bool ReadOnlyApi { get; set; }

    /// <summary>Máximo de órdenes de una exportación CSV; si el resultado es mayor, no se exporta y se avisa.</summary>
    public int ExportMaxRows { get; set; } = DefaultExportMaxRows;

    public bool IsEnabled(PortfolioFeature feature) => feature switch
    {
        PortfolioFeature.Reports => Reports,
        PortfolioFeature.PrintableSheets => PrintableSheets,
        PortfolioFeature.ReadOnlyApi => ReadOnlyApi,
        _ => false,
    };

    /// <summary>Nombres de los extras habilitados, para la bitácora de inicio.</summary>
    public IReadOnlyList<string> EnabledNames() =>
    [
        .. Enum.GetValues<PortfolioFeature>().Where(IsEnabled).Select(feature => $"{SectionName}:{feature}"),
    ];
}

public enum PortfolioFeature
{
    Reports,
    PrintableSheets,
    ReadOnlyApi,
}

/// <summary>
/// Marca un controlador como parte de un extra de portafolio: si el extra está desactivado, el controlador no se registra
/// (ver <see cref="PortfolioFeatureConvention"/>) y sus rutas responden igual que una ruta inexistente.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PortfolioFeatureAttribute(PortfolioFeature feature) : Attribute
{
    public PortfolioFeature Feature { get; } = feature;
}
