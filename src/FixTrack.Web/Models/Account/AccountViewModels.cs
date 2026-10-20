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

