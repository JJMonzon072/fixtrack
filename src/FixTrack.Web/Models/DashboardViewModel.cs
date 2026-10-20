using FixTrack.Application.Dashboard;
using FixTrack.Application.Modules;
using FixTrack.Web.Services;

namespace FixTrack.Web.Models;

/// <param name="Today">Fecha actual, ya formateada en español.</param>
/// <param name="Summary">Indicadores y listas calculados a partir de los datos registrados.</param>
/// <param name="SetupSteps">Módulos en el orden recomendado para comenzar a operar (se muestran mientras no hay órdenes).</param>
/// <param name="Application">Datos de la instancia en ejecución.</param>
public sealed record DashboardViewModel(
    string Today,
    DashboardSummary Summary,
    IReadOnlyList<ModuleInfo> SetupSteps,
    ApplicationInfo Application)
{
    public bool HasOrders => Summary.Counts.Total > 0;
}

/// <summary>Una fila de un gráfico de barras: etiqueta, cantidad y proporción respecto del total.</summary>
/// <param name="Tone">Valor de <c>data-status</c> para el color de la barra, o <c>null</c> para el color de acento.</param>
public sealed record BarItem(string Label, int Count, int Total, string? Tone = null, string? Url = null)
{
    /// <summary>Ancho de la barra en porcentaje (0 a 100).</summary>
    public int Percent => Total == 0 ? 0 : (int)Math.Round(Count * 100.0 / Total);
}
