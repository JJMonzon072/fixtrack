using System.Text;
using FixTrack.Application.Reports;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Web.Reports;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>Exportación CSV: formato compatible con Excel y protección contra la inyección de fórmulas.</summary>
public sealed class CsvTests
{
    private static readonly TimeZoneInfo Guatemala = TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala");

    [Theory]
    [InlineData("Ana Morales", "Ana Morales")]
    [InlineData("Peña, Núñez & Cía.", "\"Peña, Núñez & Cía.\"")]
    [InlineData("Pantalla \"rota\"", "\"Pantalla \"\"rota\"\"\"")]
    [InlineData("Línea 1\nLínea 2", "\"Línea 1\nLínea 2\"")]
    [InlineData("Línea 1\r\nLínea 2", "\"Línea 1\r\nLínea 2\"")]
    [InlineData(" con espacio ", "\" con espacio \"")]
    [InlineData("Ñandú 🛠️ ü", "Ñandú 🛠️ ü")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Text_fields_are_quoted_only_when_needed(string? value, string expected) =>
        Assert.Equal(expected, Csv.Text(value));

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+5555-0101")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A2)")]
    [InlineData("=HYPERLINK(\"http://ejemplo.invalid\",\"clic\")")]
    [InlineData("=cmd|' /C calc'!A0")]
    [InlineData("   =1+1")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    [InlineData("\n=1+1")]
    [InlineData("＝1+1")]
    [InlineData("＋1")]
    [InlineData("－1")]
    [InlineData("＠SUM(A1)")]
    public void Anything_a_spreadsheet_would_run_becomes_quoted_text(string value)
    {
        var field = Csv.Text(value);

        Assert.True(Csv.LooksLikeFormula(value));
        Assert.StartsWith("\"'", field, StringComparison.Ordinal);
        Assert.EndsWith("\"", field, StringComparison.Ordinal);
        Assert.Equal(value, field[2..^1].Replace("\"\"", "\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Equipo 1 = OK")]
    [InlineData("Costo: -")]
    [InlineData("correo@dominio")]
    [InlineData("2026-10-21")]
    public void Ordinary_text_with_those_characters_in_the_middle_is_not_altered(string value)
    {
        Assert.False(Csv.LooksLikeFormula(value));
        Assert.Equal(value, Csv.Text(value));
    }

    [Fact]
    public void Amounts_are_exact_with_a_decimal_point_and_dates_are_local()
    {
        Assert.Equal("1250.50", Csv.Amount(1250.5m));
        Assert.Equal("0.00", Csv.Amount(0m));
        Assert.Equal("99999999.99", Csv.Amount(99_999_999.99m));
        Assert.Equal(string.Empty, Csv.Amount(null));
        Assert.Equal("2026-10-31 23:59", Csv.LocalDateTime(new DateTime(2026, 11, 1, 5, 59, 0, DateTimeKind.Utc), Guatemala));
        Assert.Equal(string.Empty, Csv.LocalDateTime(null, Guatemala));
    }

    [Fact]
    public void The_file_is_utf8_with_bom_crlf_and_spanish_headers_without_contact_data()
    {
        ReportExportRow[] rows =
        [
            new(42, new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc), ServiceOrderStatus.Delivered, ServiceOrderPriority.Urgent,
                "Peña, Núñez", DeviceType.Laptop, "Dell", "Latitude \"5440\"", "=SN-1", "Carla Ruiz", "No enciende,\ncon olor a quemado",
                new DateTime(2026, 10, 5, 18, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 4, 18, 0, 0, DateTimeKind.Utc), 400m, 450.5m),
            new(43, new DateTime(2026, 10, 2, 6, 0, 0, DateTimeKind.Utc), ServiceOrderStatus.Received, ServiceOrderPriority.Low,
                "José", DeviceType.Printer, "HP", null, null, null, "@Atasco", null, null, null, null),
        ];

        var bytes = OrderCsv.Write(rows, Guatemala);
        var text = Encoding.UTF8.GetString(bytes.AsSpan(3));

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.StartsWith(
            "Orden,Fecha de recepción (hora local),Estado,Prioridad,Cliente,Tipo de equipo,Marca,Modelo,Número de serie,Técnico," +
            "Problema reportado,Entrega comprometida (hora local),Fecha de entrega (hora local),Costo estimado (GTQ),Costo final (GTQ)\r\n",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "OS-000042,2026-10-01 00:00,Entregada,Urgente,\"Peña, Núñez\",Laptop,Dell,\"Latitude \"\"5440\"\"\",\"'=SN-1\",Carla Ruiz," +
            "\"No enciende,\ncon olor a quemado\",2026-10-05 12:00,2026-10-04 12:00,400.00,450.50\r\n",
            text,
            StringComparison.Ordinal);
        Assert.EndsWith("OS-000043,2026-10-02 00:00,Recibida,Baja,José,Impresora,HP,,,,\"'@Atasco\",,,,\r\n", text, StringComparison.Ordinal);
        Assert.Equal(15, OrderCsv.Headers.Count);
        Assert.DoesNotContain(OrderCsv.Headers, header =>
            header.Contains("Teléfono", StringComparison.Ordinal) || header.Contains("Correo", StringComparison.Ordinal));
    }

    [Fact]
    public void The_file_name_says_which_orders_and_which_period()
    {
        var period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Guatemala);

        Assert.Equal("fixtrack-ordenes-recibidas-2026-10-01-a-2026-10-31.csv", OrderCsv.FileName(ReportExportBasis.Received, period));
        Assert.Equal("fixtrack-ordenes-entregadas-2026-10-01-a-2026-10-31.csv", OrderCsv.FileName(ReportExportBasis.Delivered, period));
    }
}
