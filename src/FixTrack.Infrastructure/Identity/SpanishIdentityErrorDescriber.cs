using Microsoft.AspNetCore.Identity;

namespace FixTrack.Infrastructure.Identity;

/// <summary>Mensajes de ASP.NET Core Identity en español.</summary>
public sealed class SpanishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "No se pudo completar la operación.");

    public override IdentityError ConcurrencyFailure() =>
        Error(nameof(ConcurrencyFailure), "Otra persona modificó la cuenta al mismo tiempo. Intenta de nuevo.");

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "La contraseña actual no es correcta.");

    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "El enlace o el código ya no es válido.");

    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), "El usuario solo puede contener letras sin tilde, números y los símbolos - . _ @ +.");

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), "Ingresa un correo electrónico válido, por ejemplo nombre@dominio.com.");

    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), "Ya existe una cuenta con este correo electrónico.");

    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), "Ya existe una cuenta con este correo electrónico.");

    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), "El rol no es válido.");

    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), "El rol ya existe.");

    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "La cuenta ya tiene una contraseña.");

    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "La cuenta no admite bloqueo.");

    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), "La cuenta ya tiene ese rol.");

    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), "La cuenta no tiene ese rol.");

    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), $"La contraseña debe tener al menos {length} caracteres.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), $"La contraseña debe tener al menos {uniqueChars} caracteres distintos.");

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), "La contraseña debe incluir al menos un símbolo.");

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), "La contraseña debe incluir al menos un número.");

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), "La contraseña debe incluir al menos una letra minúscula.");

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), "La contraseña debe incluir al menos una letra mayúscula.");

    public override IdentityError RecoveryCodeRedemptionFailed() =>
        Error(nameof(RecoveryCodeRedemptionFailed), "El código de recuperación no es válido.");

    public override IdentityError LoginAlreadyAssociated() =>
        Error(nameof(LoginAlreadyAssociated), "Ese acceso externo ya está asociado a otra cuenta.");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
