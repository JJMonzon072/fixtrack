using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FixTrack.Infrastructure.Persistence;

/// <summary>
/// SQL Server no guarda la zona horaria en datetime2. Al leer, las fechas se marcan como UTC
/// para que el resto de la aplicación nunca reciba fechas de tipo <see cref="DateTimeKind.Unspecified"/>.
/// </summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
