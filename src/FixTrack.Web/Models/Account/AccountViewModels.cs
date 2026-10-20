namespace FixTrack.Web.Models.Account;

/// <summary>Formulario de inicio de sesión. La contraseña nunca se devuelve a la vista.</summary>
public sealed class LoginFormModel
{
    public string? Email { get; set; }

    public string? Password { get; set; }
}

/// <param name="ReturnUrl">Página local a la que se vuelve después de iniciar sesión.</param>
/// <param name="Error">Mensaje general (por ejemplo, credenciales incorrectas o cuenta bloqueada).</param>
/// <param name="Notice">Aviso informativo (por ejemplo, después de cerrar sesión).</param>
public sealed record LoginViewModel(string? Email, string? ReturnUrl, string? Error, string? Notice, ApplicationDetails Application);

/// <summary>Datos de la instancia que se muestran en la pantalla de acceso.</summary>
public sealed record ApplicationDetails(string Version, string EnvironmentName, bool IsDevelopment);

public sealed class ChangePasswordFormModel
{
    public string? CurrentPassword { get; set; }

    public string? Password { get; set; }

    public string? ConfirmPassword { get; set; }
}

/// <summary>Formulario de cuenta. Al editar se ignoran el correo y las contraseñas.</summary>
public sealed class UserFormModel
{
    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Role { get; set; }

    public string? Password { get; set; }

    public string? ConfirmPassword { get; set; }
}

public sealed class ResetPasswordFormModel
{
    public string? Password { get; set; }

    public string? ConfirmPassword { get; set; }
}

public sealed record UsersIndexViewModel(IReadOnlyList<FixTrack.Application.Security.UserAccount> Accounts, string CurrentUserId);

/// <summary>Registro (Account es null) o edición de una cuenta.</summary>
public sealed record UserEditorViewModel(FixTrack.Application.Security.UserAccount? Account, UserFormModel Form, bool IsCurrentUser)
{
    public bool IsNew => Account is null;
}

public sealed record ResetPasswordViewModel(FixTrack.Application.Security.UserAccount Account);
