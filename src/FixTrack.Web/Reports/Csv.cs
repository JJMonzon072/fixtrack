using System.Globalization;
using System.Text;
using FixTrack.Application.Reports;
using FixTrack.Web.Models;

namespace FixTrack.Web.Reports;

/// <summary>
/// Formato CSV compatible con Excel y con RFC 4180: separador coma, fin de línea CRLF, UTF-8 con BOM (para que Excel
/// reconozca las tildes) y comillas dobles alrededor de los campos con comas, comillas o saltos de línea.
/// <para>
/// Protección contra inyección de fórmulas (CSV Formula Injection, OWASP): un campo escrito por personas que empieza con
/// <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c> (o sus variantes de ancho completo), tabulador o salto de línea, aunque vaya
/// precedido de espacios, se antepone con un apóstrofo y se encierra entre comillas: la hoja de cálculo lo muestra como
/// texto y nunca lo ejecuta.
/// </para>
/// </summary>
public static class Csv
{
    public const string ContentType = "text/csv; charset=utf-8";

    private const char Separator = ',';
    private const string NewLine = "\r\n";

    /// <summary>Caracteres con los que una hoja de cálculo interpreta una celda como fórmula.</summary>
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '＝', '＋', '－', '＠'];

    /// <summary>¿Se interpretaría el texto como fórmula al abrir el archivo?</summary>
    public static bool LooksLikeFormula(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        if (value[0] is '\t' or '\r' or '\n')
        {
            return true;
        }

        var trimmed = value.TrimStart();
        return trimmed.Length > 0 && Array.IndexOf(FormulaStarts, trimmed[0]) >= 0;
    }

    /// <summary>Campo de texto escrito por personas (nombres, problema reportado, series): se neutralizan las fórmulas.</summary>
    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (LooksLikeFormula(value))
        {
            return Quote("'" + value);
        }

        return value.IndexOfAny([Separator, '"', '\r', '\n']) >= 0 || value != value.Trim() ? Quote(value) : value;
    }

    /// <summary>Monto exacto con punto decimal y dos decimales, sin símbolo ni separador de miles (por ejemplo 1250.00).</summary>
    public static string Amount(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Fecha y hora local con el formato AAAA-MM-DD HH:MM, que Excel reconoce como fecha.</summary>
    public static string LocalDateTime(DateTime? utc, TimeZoneInfo timeZone) =>
        utc is { } value
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), timeZone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>Una línea con los campos ya formateados, terminada en CRLF.</summary>
    public static string Line(IEnumerable<string> fields) => string.Join(Separator, fields) + NewLine;

    /// <summary>Contenido del archivo: BOM de UTF-8 seguido del texto.</summary>
    public static byte[] Encode(string content) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)];

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

/// <summary>Exportación de órdenes del centro de reportes en CSV.</summary>
public static class OrderCsv
{
    public static IReadOnlyList<string> Headers { get; } =
    [
        "Orden",
        "Fecha de recepción (hora local)",
        "Estado",
        "Prioridad",
        "Cliente",
        "Tipo de equipo",
        "Marca",
        "Modelo",
        "Número de serie",
        "Técnico",
        "Problema reportado",
        "Entrega comprometida (hora local)",
        "Fecha de entrega (hora local)",
        "Costo estimado (GTQ)",
        "Costo final (GTQ)",
    ];

    /// <summary>Archivo completo: encabezados en español y una línea por orden. No se guarda en el servidor.</summary>
    public static byte[] Write(IReadOnlyList<ReportExportRow> rows, TimeZoneInfo timeZone)
    {
        var content = new StringBuilder(Csv.Line(Headers.Select(Csv.Text)));
        foreach (var row in rows)
        {
            content.Append(Csv.Line(
            [
                row.Number,
                Csv.LocalDateTime(row.ReceivedAtUtc, timeZone),
                DomainDisplay.Name(row.Status),
                DomainDisplay.Name(row.Priority),
                Csv.Text(row.CustomerName),
                DomainDisplay.Name(row.DeviceType),
                Csv.Text(row.DeviceBrand),
                Csv.Text(row.DeviceModel),
                Csv.Text(row.SerialNumber),
                Csv.Text(row.TechnicianName),
                Csv.Text(row.ReportedIssue),
                Csv.LocalDateTime(row.PromisedAtUtc, timeZone),
                Csv.LocalDateTime(row.DeliveredAtUtc, timeZone),
                Csv.Amount(row.EstimatedCost),
                Csv.Amount(row.FinalCost),
            ]));
        }

        return Csv.Encode(content.ToString());
    }

    /// <summary>Nombre del archivo, por ejemplo fixtrack-ordenes-recibidas-2026-10-01-a-2026-10-31.csv.</summary>
    public static string FileName(ReportExportBasis basis, ReportPeriod period) =>
        $"fixtrack-ordenes-{(basis == ReportExportBasis.Delivered ? "entregadas" : "recibidas")}-{period.From:yyyy-MM-dd}-a-{period.To:yyyy-MM-dd}.csv";
}
