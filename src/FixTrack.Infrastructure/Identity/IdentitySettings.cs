using Microsoft.AspNetCore.Identity;

namespace FixTrack.Infrastructure.Identity;

/// <summary>Política de contraseñas, bloqueo y usuarios de FixTrack (la misma en la aplicación y en los comandos).</summary>
public static class IdentitySettings
{
    public const int MinPasswordLength = 12;
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static void Configure(IdentityOptions options)
    {
        // Contraseñas largas: 12 caracteres con mayúsculas, minúsculas y números. Los símbolos son opcionales.
        options.Password.RequiredLength = MinPasswordLength;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 5;

        // Bloqueo temporal tras intentos fallidos, también para cuentas nuevas.
        options.Lockout.MaxFailedAccessAttempts = MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    }
}
