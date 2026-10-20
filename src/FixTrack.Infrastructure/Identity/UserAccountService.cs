using FixTrack.Application.Common;
using FixTrack.Application.Security;
using FixTrack.Domain.Common;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Identity;

/// <summary>
/// Administración de cuentas con ASP.NET Core Identity. Una cuenta tiene exactamente un rol. Deshabilitar una
/// cuenta equivale a bloquearla indefinidamente; cambiar su rol, su contraseña o su estado renueva su sello de
/// seguridad, de modo que las sesiones abiertas se cierran en la siguiente validación de la cookie.
/// </summary>
internal sealed class UserAccountService(
    UserManager<FixTrackUser> users,
    FixTrackDbContext db,
    TimeProvider clock) : IUserAccountService
{
    internal const string FullNameField = "FullName";
    internal const string EmailField = "Email";
    internal const string RoleField = "Role";
    internal const string PasswordField = "Password";
    internal const string ConfirmPasswordField = "ConfirmPassword";
    internal const string CurrentPasswordField = "CurrentPassword";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var rows = await (
                from user in db.Users.AsNoTracking()
                join link in db.UserRoles on user.Id equals link.UserId into links
                from link in links.DefaultIfEmpty()
                join role in db.Roles on link.RoleId equals role.Id into roles
                from role in roles.DefaultIfEmpty()
                orderby user.FullName, user.Email
                select new { user.Id, user.FullName, user.Email, Role = role.Name, user.LockoutEnd, user.CreatedAtUtc, user.LastSignInAtUtc })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => ToAccount(row.Id, row.FullName, row.Email, row.Role, row.LockoutEnd, row.CreatedAtUtc, row.LastSignInAtUtc, now))];
    }

    public async Task<UserAccount?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return null;
        }

        var role = (await users.GetRolesAsync(user)).FirstOrDefault();
        return ToAccount(user.Id, user.FullName, user.Email, role, user.LockoutEnd, user.CreatedAtUtc, user.LastSignInAtUtc, clock.GetUtcNow());
    }

    public async Task<CommandResult<string>> CreateAsync(NewUserAccountInput input, CancellationToken cancellationToken)
    {
        var fullName = Text.Clean(input.FullName);
        var email = ContactRules.NormalizeEmail(input.Email);
        var errors = new Dictionary<string, string>();
        AddNameError(errors, fullName);
        AddRoleError(errors, input.Role);

        if (email is null)
        {
            errors[EmailField] = "El correo electrónico es obligatorio: es el usuario para iniciar sesión.";
        }
        else if (ContactRules.ValidateEmail(email) is { } emailError)
        {
            errors[EmailField] = emailError;
        }
        else if (await users.FindByEmailAsync(email) is not null)
        {
            errors[EmailField] = "Ya existe una cuenta con este correo electrónico.";
        }

        var user = new FixTrackUser { FullName = fullName, Email = email, UserName = email, CreatedAtUtc = UtcNow };
        await AddPasswordErrorsAsync(errors, user, input.Password, input.ConfirmPassword);
        if (errors.Count > 0)
        {
            return CommandResult<string>.Invalid(errors);
        }

        var created = await users.CreateAsync(user, input.Password!);
        if (!created.Succeeded)
        {
            return CommandResult<string>.Invalid(ToErrors(created, EmailField));
        }

        var added = await users.AddToRoleAsync(user, input.Role!);
        return added.Succeeded
            ? CommandResult<string>.Success(user.Id)
            : CommandResult<string>.Invalid(ToErrors(added, RoleField));
    }

    public async Task<CommandResult> UpdateAsync(string id, UserAccountInput input, string actingUserId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return CommandResult.NotFound();
        }

        var fullName = Text.Clean(input.FullName);
        var errors = new Dictionary<string, string>();
        AddNameError(errors, fullName);
        AddRoleError(errors, input.Role);
        if (errors.Count > 0)
        {
            return CommandResult.Invalid(errors);
        }

        var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault();
        var roleChanges = currentRole != input.Role;
        if (roleChanges && id == actingUserId)
        {
            return CommandResult.Conflict("No puedes cambiar el rol de tu propia cuenta. Pídeselo a otro administrador.");
        }

        if (roleChanges && currentRole == Roles.Administrator && IsEnabled(user) && await CountEnabledAdministratorsAsync(cancellationToken) <= 1)
        {
            return CommandResult.Conflict("Es el único administrador habilitado: asigna antes el rol de administrador a otra cuenta.");
        }

        user.FullName = fullName;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return CommandResult.Conflict(updated.Errors.First().Description);
        }

        if (roleChanges)
        {
            if (currentRole is not null)
            {
                await users.RemoveFromRoleAsync(user, currentRole);
            }

            await users.AddToRoleAsync(user, input.Role!);
            await users.UpdateSecurityStampAsync(user);
        }

        return CommandResult.Success();
    }

    public async Task<CommandResult> SetEnabledAsync(string id, bool enabled, string actingUserId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return CommandResult.NotFound();
        }

        if (IsEnabled(user) == enabled)
        {
            return CommandResult.Conflict(enabled ? "La cuenta ya estaba habilitada." : "La cuenta ya estaba deshabilitada.");
        }

        if (!enabled)
        {
            if (id == actingUserId)
            {
                return CommandResult.Conflict("No puedes deshabilitar tu propia cuenta.");
            }

            if (await users.IsInRoleAsync(user, Roles.Administrator) && await CountEnabledAdministratorsAsync(cancellationToken) <= 1)
            {
                return CommandResult.Conflict("Es el único administrador habilitado: no puede deshabilitarse.");
            }
        }

        // Una cuenta deshabilitada queda bloqueada sin fecha de fin; al habilitarla se limpian los intentos fallidos.
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, enabled ? null : DateTimeOffset.MaxValue);
        await users.ResetAccessFailedCountAsync(user);
        await users.UpdateSecurityStampAsync(user);
        return CommandResult.Success();
    }

    public async Task<CommandResult> ResetPasswordAsync(string id, string? password, string? confirmPassword, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return CommandResult.NotFound();
        }

        var errors = new Dictionary<string, string>();
        await AddPasswordErrorsAsync(errors, user, password, confirmPassword);
        if (errors.Count > 0)
        {
            return CommandResult.Invalid(errors);
        }

        // El token de restablecimiento valida la contraseña nueva antes de reemplazar la anterior.
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password!);
        if (!reset.Succeeded)
        {
            return CommandResult.Invalid(ToErrors(reset, PasswordField));
        }

        await users.ResetAccessFailedCountAsync(user);
        return CommandResult.Success();
    }

    public async Task<CommandResult> ChangeOwnPasswordAsync(
        string userId,
        string? currentPassword,
        string? password,
        string? confirmPassword,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return CommandResult.NotFound();
        }

        var errors = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(currentPassword))
        {
            errors[CurrentPasswordField] = "Escribe tu contraseña actual.";
        }

        await AddPasswordErrorsAsync(errors, user, password, confirmPassword);
        if (errors.Count == 0 && currentPassword == password)
        {
            errors[PasswordField] = "La contraseña nueva debe ser distinta de la actual.";
        }

        if (errors.Count > 0)
        {
            return CommandResult.Invalid(errors);
        }

        // Probar contraseñas desde una sesión abierta también cuenta para el bloqueo: si la cuenta está bloqueada, no se
        // comprueba la contraseña actual.
        if (await users.IsLockedOutAsync(user))
        {
            return CommandResult.Invalid(new Dictionary<string, string>
            {
                [CurrentPasswordField] = "Por seguridad, el cambio de contraseña está bloqueado unos minutos. Intenta de nuevo más tarde.",
            });
        }

        var changed = await users.ChangePasswordAsync(user, currentPassword!, password!);
        if (changed.Succeeded)
        {
            await users.ResetAccessFailedCountAsync(user);
            return CommandResult.Success();
        }

        var mismatch = changed.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch));
        if (mismatch)
        {
            await users.AccessFailedAsync(user);
        }

        return CommandResult.Invalid(ToErrors(changed, mismatch ? CurrentPasswordField : PasswordField));
    }

    public async Task RecordSignInAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is not null)
        {
            user.LastSignInAtUtc = UtcNow;
            await users.UpdateAsync(user);
        }
    }

    public async Task<InitialAdministratorResult> CreateInitialAdministratorAsync(
        string? email,
        string? fullName,
        string? password,
        CancellationToken cancellationToken)
    {
        if (await CountEnabledAdministratorsAsync(cancellationToken) > 0)
        {
            return new InitialAdministratorResult(InitialAdministratorOutcome.AlreadyExists, []);
        }

        var created = await CreateAsync(new NewUserAccountInput(fullName, email, Roles.Administrator, password, password), cancellationToken);
        return created.Succeeded
            ? new InitialAdministratorResult(InitialAdministratorOutcome.Created, [])
            : new InitialAdministratorResult(InitialAdministratorOutcome.Invalid, [.. created.Errors.Values]);
    }

    /// <summary>
    /// Administradores habilitados. Una cuenta deshabilitada tiene como fin de bloqueo <see cref="DateTimeOffset.MaxValue"/>;
    /// un bloqueo temporal por intentos fallidos no la deshabilita. Se compara por igualdad para que la consulta
    /// funcione igual en SQL Server y en SQLite.
    /// </summary>
    private Task<int> CountEnabledAdministratorsAsync(CancellationToken cancellationToken) =>
        (from link in db.UserRoles
         join role in db.Roles on link.RoleId equals role.Id
         join user in db.Users on link.UserId equals user.Id
         where role.Name == Roles.Administrator && (user.LockoutEnd == null || user.LockoutEnd != DateTimeOffset.MaxValue)
         select user.Id)
        .CountAsync(cancellationToken);

    private static bool IsEnabled(FixTrackUser user) => user.LockoutEnd != DateTimeOffset.MaxValue;

    private static UserAccount ToAccount(
        string id,
        string fullName,
        string? email,
        string? role,
        DateTimeOffset? lockoutEnd,
        DateTime createdAtUtc,
        DateTime? lastSignInAtUtc,
        DateTimeOffset now)
    {
        var disabled = lockoutEnd == DateTimeOffset.MaxValue;
        var lockedOut = !disabled && lockoutEnd is { } until && until > now;
        return new UserAccount(id, fullName, email ?? string.Empty, role, !disabled, lockedOut, createdAtUtc, lastSignInAtUtc);
    }

    private static void AddNameError(Dictionary<string, string> errors, string fullName)
    {
        if (ContactRules.ValidateName(fullName) is { } nameError)
        {
            errors[FullNameField] = nameError;
        }
    }

    private static void AddRoleError(Dictionary<string, string> errors, string? role)
    {
        if (!Roles.IsValid(role))
        {
            errors[RoleField] = "Selecciona el rol de la cuenta.";
        }
    }

    /// <summary>Valida la contraseña con la política de Identity sin guardar nada.</summary>
    private async Task AddPasswordErrorsAsync(Dictionary<string, string> errors, FixTrackUser user, string? password, string? confirmPassword)
    {
        if (string.IsNullOrEmpty(password))
        {
            errors[PasswordField] = "Escribe la contraseña.";
            return;
        }

        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            if (!result.Succeeded)
            {
                errors[PasswordField] = string.Join(" ", result.Errors.Select(error => error.Description));
                return;
            }
        }

        if (password != confirmPassword)
        {
            errors[ConfirmPasswordField] = "Las contraseñas no coinciden.";
        }
    }

    private static Dictionary<string, string> ToErrors(IdentityResult result, string field) =>
        new() { [field] = string.Join(" ", result.Errors.Select(error => error.Description)) };
}
