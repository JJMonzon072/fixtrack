using FixTrack.Application.Common;

namespace FixTrack.Application.Security;

/// <summary>
/// Administración de las cuentas de acceso. Las contraseñas solo se reciben para guardarlas como hash: nunca se
/// devuelven ni se registran en bitácoras. Siempre debe quedar al menos un administrador habilitado.
/// Cada cambio administrativo es atómico y se serializa con los demás cambios de cuentas, de modo que dos
/// solicitudes simultáneas no pueden dejar la instalación sin administradores ni una cuenta a medio cambiar.
/// <c>actingUserId</c> es la cuenta que hace el cambio: debe seguir siendo un administrador habilitado en ese momento,
/// aunque su sesión diga lo contrario.
/// </summary>
public interface IUserAccountService
{
    /// <summary>Cuentas ordenadas por nombre, con su rol, en una sola consulta.</summary>
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken);

    Task<UserAccount?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>Crea una cuenta habilitada con un rol. Si tiene éxito, devuelve su Id.</summary>
    Task<CommandResult<string>> CreateAsync(NewUserAccountInput input, string actingUserId, CancellationToken cancellationToken);

    /// <summary>Cambia el nombre y el rol. Nadie puede cambiar su propio rol.</summary>
    Task<CommandResult> UpdateAsync(string id, UserAccountInput input, string actingUserId, CancellationToken cancellationToken);

    /// <summary>Habilita o deshabilita la cuenta. Deshabilitarla cierra sus sesiones abiertas.</summary>
    Task<CommandResult> SetEnabledAsync(string id, bool enabled, string actingUserId, CancellationToken cancellationToken);

    /// <summary>Quita el bloqueo temporal por intentos fallidos de una cuenta habilitada.</summary>
    Task<CommandResult> UnlockAsync(string id, string actingUserId, CancellationToken cancellationToken);

    /// <summary>
    /// El administrador define una contraseña nueva para otra cuenta y quita su bloqueo temporal. Cierra las sesiones
    /// abiertas de la cuenta. La propia contraseña se cambia con <see cref="ChangeOwnPasswordAsync"/>.
    /// </summary>
    Task<CommandResult> ResetPasswordAsync(
        string id,
        string? password,
        string? confirmPassword,
        string actingUserId,
        CancellationToken cancellationToken);

    /// <summary>El propio usuario cambia su contraseña indicando la actual.</summary>
    Task<CommandResult> ChangeOwnPasswordAsync(
        string userId,
        string? currentPassword,
        string? password,
        string? confirmPassword,
        CancellationToken cancellationToken);

    Task RecordSignInAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Crea el primer administrador con datos tomados de secretos o variables de entorno. No hace nada si ya existe
    /// un administrador habilitado, de modo que no puede usarse para tomar el control de una instalación.
    /// </summary>
    Task<InitialAdministratorResult> CreateInitialAdministratorAsync(
        string? email,
        string? fullName,
        string? password,
        CancellationToken cancellationToken);
}
