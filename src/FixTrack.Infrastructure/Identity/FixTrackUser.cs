using Microsoft.AspNetCore.Identity;

namespace FixTrack.Infrastructure.Identity;

/// <summary>
/// Cuenta de acceso a FixTrack (tabla AspNetUsers). El correo electrónico es también el nombre de usuario.
/// La contraseña se guarda solo como hash de ASP.NET Core Identity (PBKDF2 con sal).
/// </summary>
public sealed class FixTrackUser : IdentityUser
{
    public const int FullNameMaxLength = 150;

    /// <summary>Nombre visible en la interfaz.</summary>
    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastSignInAtUtc { get; set; }
}
