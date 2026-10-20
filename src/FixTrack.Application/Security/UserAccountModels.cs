namespace FixTrack.Application.Security;

/// <summary>Cuenta de acceso tal como se muestra en la administración de usuarios.</summary>
/// <param name="IsEnabled">Las cuentas no se eliminan: se deshabilitan y dejan de poder iniciar sesión.</param>
/// <param name="IsLockedOut">Bloqueo temporal por intentos fallidos (no aplica a cuentas deshabilitadas).</param>
public sealed record UserAccount(
    string Id,
    string FullName,
    string Email,
    string? Role,
    bool IsEnabled,
    bool IsLockedOut,
    DateTime CreatedAtUtc,
    DateTime? LastSignInAtUtc);

/// <summary>Datos de una cuenta nueva. La contraseña inicial la define el administrador.</summary>
public sealed record NewUserAccountInput(string? FullName, string? Email, string? Role, string? Password, string? ConfirmPassword);

/// <summary>Datos editables de una cuenta: el correo, que es el usuario de acceso, no cambia.</summary>
public sealed record UserAccountInput(string? FullName, string? Role);

/// <summary>Resultado del comando que crea el primer administrador.</summary>
public enum InitialAdministratorOutcome
{
    Created,

    /// <summary>Ya existe un administrador habilitado: el comando no crea otro.</summary>
    AlreadyExists,

    Invalid,
}

public sealed record InitialAdministratorResult(InitialAdministratorOutcome Outcome, IReadOnlyList<string> Errors);
