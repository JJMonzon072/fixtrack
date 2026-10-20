using System.Security.Cryptography;
using FixTrack.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace FixTrack.Web.Security;

/// <summary>
/// Hace que un inicio de sesión fallido cueste lo mismo exista o no la cuenta, para que el tiempo de respuesta no revele
/// qué correos están registrados:
/// <list type="bullet">
/// <item>Si el correo no tiene cuenta, o la cuenta está bloqueada o deshabilitada (Identity rechaza sin calcular el hash),
/// se verifica la contraseña contra un hash ficticio con el mismo algoritmo y las mismas iteraciones.</item>
/// <item>Toda respuesta fallida tarda al menos <c>Security:FailedLoginMinimumMilliseconds</c> (500 ms por defecto), lo que
/// también oculta el viaje a la base que solo ocurre con cuentas reales (el contador de intentos fallidos).</item>
/// </list>
/// </summary>
public sealed class FailedLoginEqualizer(IPasswordHasher<FixTrackUser> hasher, IConfiguration configuration, TimeProvider clock)
{
    public const string MinimumDurationSetting = "Security:FailedLoginMinimumMilliseconds";

    private static readonly FixTrackUser DummyUser = new() { FullName = "-" };
    private static string? s_dummyHash;

    private readonly TimeSpan _minimum = TimeSpan.FromMilliseconds(Math.Max(0, configuration.GetValue(MinimumDurationSetting, 500)));

    /// <summary>Marca el inicio de un intento para medir su duración.</summary>
    public long Start() => clock.GetTimestamp();

    /// <summary>Calcula un hash de contraseña equivalente al de una cuenta real y descarta el resultado.</summary>
    public void SimulatePasswordCheck(string password)
    {
        // El hash ficticio se calcula una sola vez con el mismo hasher que usan las cuentas reales.
        var dummyHash = LazyInitializer.EnsureInitialized(
            ref s_dummyHash,
            () => hasher.HashPassword(DummyUser, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
        hasher.VerifyHashedPassword(DummyUser, dummyHash, password);
    }

    /// <summary>Completa el tiempo mínimo de una respuesta fallida.</summary>
    public Task CompleteFailureAsync(long startedAt, CancellationToken cancellationToken)
    {
        var remaining = _minimum - clock.GetElapsedTime(startedAt);
        return remaining > TimeSpan.Zero ? Task.Delay(remaining, clock, cancellationToken) : Task.CompletedTask;
    }
}
