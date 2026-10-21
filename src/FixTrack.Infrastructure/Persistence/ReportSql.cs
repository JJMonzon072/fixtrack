using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace FixTrack.Infrastructure.Persistence;

/// <summary>
/// Funciones SQL de los reportes. EF Core no traduce la diferencia entre dos fechas (<see cref="TimeSpan"/>) dentro de
/// una agregación; con <see cref="UnixSeconds"/> la base calcula duraciones como restas de enteros y las suma en la
/// misma consulta, sin traer las órdenes a memoria.
/// </summary>
public static class ReportSql
{
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>
    /// Segundos transcurridos desde 1970-01-01 00:00 UTC hasta <paramref name="utc"/>. Solo puede usarse dentro de una
    /// consulta LINQ: en SQL Server se traduce a <c>DATEDIFF_BIG(SECOND, '1970-01-01T00:00:00', fecha)</c> y en SQLite (las
    /// pruebas) a <c>unixepoch(fecha)</c>.
    /// </summary>
    public static long UnixSeconds(DateTime utc) =>
        throw new NotSupportedException($"{nameof(ReportSql)}.{nameof(UnixSeconds)} solo se usa dentro de consultas de EF Core.");

    /// <summary>Registra la traducción de <see cref="UnixSeconds"/> para el proveedor del contexto.</summary>
    internal static void Register(ModelBuilder modelBuilder, string? providerName)
    {
        var method = typeof(ReportSql).GetMethod(nameof(UnixSeconds))!;
        if (providerName == SqlServerProvider)
        {
            var bigint = new LongTypeMapping("bigint", DbType.Int64);
            modelBuilder.HasDbFunction(method).HasTranslation(arguments => new SqlFunctionExpression(
                "DATEDIFF_BIG",
                [new SqlFragmentExpression("SECOND"), new SqlFragmentExpression("'1970-01-01T00:00:00'"), arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [false, false, true],
                typeof(long),
                bigint));
        }
        else
        {
            var integer = new LongTypeMapping("INTEGER", DbType.Int64);
            modelBuilder.HasDbFunction(method).HasTranslation(arguments => new SqlFunctionExpression(
                "unixepoch",
                [arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(long),
                integer));
        }
    }
}
