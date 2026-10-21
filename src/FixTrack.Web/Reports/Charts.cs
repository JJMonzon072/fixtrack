using System.Globalization;
using FixTrack.Application.Reports;

namespace FixTrack.Web.Reports;

/// <summary>Tamaño de dibujo de un gráfico: uno ancho para escritorio y uno estrecho para teléfonos.</summary>
public sealed record ChartSize(string Name, int Width, int Height, int PadLeft, int MaxXLabels)
{
    public const int PadRight = 10;
    public const int PadTop = 12;
    public const int PadBottom = 26;

    public static ChartSize Wide { get; } = new("ancho", 640, 240, 52, 12);

    public static ChartSize Narrow { get; } = new("estrecho", 360, 230, 46, 5);

    public static IReadOnlyList<ChartSize> All { get; } = [Wide, Narrow];

    public double PlotLeft => PadLeft;

    public double PlotTop => PadTop;

    public double PlotWidth => Width - PadLeft - PadRight;

    public double PlotHeight => Height - PadTop - PadBottom;

    public double Baseline => PadTop + PlotHeight;
}

public sealed record ChartTick(double Y, string Label);

public sealed record ChartLabel(double X, string Text);

/// <summary>Una barra. <paramref name="Series"/> es 0 (recibidas) o 1 (entregadas).</summary>
public sealed record ChartBar(double X, double Y, double Width, double Height, int Series, string Title);

public sealed record ChartPoint(double X, double Y, string Title);

/// <summary>Geometría de un gráfico ya calculada para un tamaño. Los números se escriben con punto decimal.</summary>
public sealed record ChartGeometry(
    ChartSize Size,
    IReadOnlyList<ChartTick> Ticks,
    IReadOnlyList<ChartLabel> Labels,
    IReadOnlyList<ChartBar> Bars,
    IReadOnlyList<ChartPoint> Points,
    string? LinePath,
    string? AreaPath)
{
    public static string N(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>
/// Construye los gráficos del centro de reportes como SVG generado en el servidor: sin bibliotecas de JavaScript, sin
/// estilos en línea (la CSP los prohíbe) y con colores tomados de las variables del tema.
/// </summary>
public static class ReportCharts
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");
    private static readonly double[] NiceSteps = [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];

    /// <summary>Recibidas y entregadas por tramo, como barras agrupadas.</summary>
    public static ChartGeometry Volume(IReadOnlyList<ReportSeriesPoint> series, ReportGranularity granularity, ChartSize size)
    {
        var max = NiceMax(series.Count == 0 ? 0 : series.Max(point => Math.Max(point.Received, point.Delivered)), wholeNumbers: true);
        var slot = size.PlotWidth / Math.Max(1, series.Count);
        var barWidth = Math.Clamp(slot * 0.34, 1.5, 16);
        var bars = new List<ChartBar>();
        for (var index = 0; index < series.Count; index++)
        {
            var point = series[index];
            var center = size.PlotLeft + (slot * index) + (slot / 2);
            var label = LongLabel(point, granularity);
            bars.Add(Bar(center - barWidth - 0.5, barWidth, point.Received, max, size, 0, $"{label}: {Plural(point.Received, "recibida", "recibidas")}"));
            bars.Add(Bar(center + 0.5, barWidth, point.Delivered, max, size, 1, $"{label}: {Plural(point.Delivered, "entregada", "entregadas")}"));
        }

        return new ChartGeometry(size, Ticks(max, size, value => value.ToString("0", CultureInfo.InvariantCulture)), Labels(series, granularity, size), bars, [], null, null);
    }

    /// <summary>Ingresos registrados por tramo, como línea con área.</summary>
    public static ChartGeometry Revenue(IReadOnlyList<ReportSeriesPoint> series, ReportGranularity granularity, ChartSize size)
    {
        var max = NiceMax(series.Count == 0 ? 0 : (double)series.Max(point => point.Revenue), wholeNumbers: false);
        var slot = size.PlotWidth / Math.Max(1, series.Count);
        var points = series
            .Select((point, index) => new ChartPoint(
                size.PlotLeft + (slot * index) + (slot / 2),
                size.Baseline - ((double)point.Revenue / max * size.PlotHeight),
                $"{LongLabel(point, granularity)}: {Models.Money.Format(point.Revenue)}"))
            .ToList();

        string? line = null, area = null;
        if (points.Count > 0)
        {
            line = "M" + string.Join(" L", points.Select(point => $"{ChartGeometry.N(point.X)} {ChartGeometry.N(point.Y)}"));
            area = $"{line} L{ChartGeometry.N(points[^1].X)} {ChartGeometry.N(size.Baseline)} L{ChartGeometry.N(points[0].X)} {ChartGeometry.N(size.Baseline)} Z";
        }

        return new ChartGeometry(size, Ticks(max, size, MoneyTick), Labels(series, granularity, size), [], points, line, area);
    }

    /// <summary>
    /// Máximo "redondo" del eje: 4 divisiones de 1, 1.5, 2, 2.5, 3, 4, 5, 6 u 8 por una potencia de 10 (en cantidades,
    /// enteras). Sin datos, el eje llega a 4 (o a Q 100) para que el gráfico vacío siga teniendo escala.
    /// </summary>
    public static double NiceMax(double value, bool wholeNumbers)
    {
        if (value <= 0)
        {
            return wholeNumbers ? 4 : 100;
        }

        var rawStep = value / 4;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var step = NiceSteps.Select(multiplier => multiplier * magnitude).First(candidate => candidate >= rawStep);
        if (wholeNumbers)
        {
            step = Math.Max(1, Math.Ceiling(step));
        }

        return step * 4;
    }

    /// <summary>Etiqueta corta de un tramo para el eje: "12 oct", "29 sep" (inicio de la semana) u "oct 26".</summary>
    public static string ShortLabel(ReportSeriesPoint point, ReportGranularity granularity) => granularity switch
    {
        ReportGranularity.Month => Clean(point.Start.ToString("MMM yy", Spanish)),
        _ => Clean(point.Start.ToString("d MMM", Spanish)),
    };

    /// <summary>Descripción completa de un tramo: "12 oct 2026", "semana del 29 sep al 5 oct 2026" u "octubre 2026".</summary>
    public static string LongLabel(ReportSeriesPoint point, ReportGranularity granularity) => granularity switch
    {
        ReportGranularity.Day => Clean(point.Start.ToString("d MMM yyyy", Spanish)),
        ReportGranularity.Week => point.Start == point.End
            ? Clean(point.Start.ToString("d MMM yyyy", Spanish))
            : $"semana del {Clean(point.Start.ToString("d MMM", Spanish))} al {Clean(point.End.ToString("d MMM yyyy", Spanish))}",
        _ => point.Start.ToString("MMMM yyyy", Spanish),
    };

    public static string Plural(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

    private static ChartBar Bar(double x, double width, int value, double max, ChartSize size, int series, string title)
    {
        // Un valor distinto de cero siempre se ve (al menos 2 px); el cero no se dibuja.
        var height = value == 0 ? 0 : Math.Max(2, value / max * size.PlotHeight);
        return new ChartBar(x, size.Baseline - height, width, height, series, title);
    }

    private static List<ChartTick> Ticks(double max, ChartSize size, Func<double, string> format) =>
    [
        .. Enumerable.Range(0, 5).Select(step => new ChartTick(size.Baseline - (step / 4d * size.PlotHeight), format(max * step / 4))),
    ];

    private static List<ChartLabel> Labels(IReadOnlyList<ReportSeriesPoint> series, ReportGranularity granularity, ChartSize size)
    {
        var every = Math.Max(1, (int)Math.Ceiling(series.Count / (double)size.MaxXLabels));
        var slot = size.PlotWidth / Math.Max(1, series.Count);
        return
        [
            .. series
                .Select((point, index) => (point, index))
                .Where(item => item.index % every == 0)
                .Select(item => new ChartLabel(size.PlotLeft + (slot * item.index) + (slot / 2), ShortLabel(item.point, granularity))),
        ];
    }

    private static string MoneyTick(double value) =>
        value >= 10_000 ? $"Q {(value / 1000).ToString("0.#", CultureInfo.InvariantCulture)}k" : $"Q {value.ToString("#,0", CultureInfo.InvariantCulture)}";

    private static string Clean(string text) => text.Replace(".", string.Empty, StringComparison.Ordinal);
}
