using FixTrack.Application.Common;
using FixTrack.Application.Security;
using FixTrack.Domain.Auditing;
using FixTrack.Domain.Common;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Identity;

/// <summary>
/// Administración de cuentas con ASP.NET Core Identity. Una cuenta tiene exactamente un rol. Deshabilitar una
/// cuenta equivale a bloquearla indefinidamente; cambiar su rol, su contraseña o su estado renueva su sello de
/// seguridad, de modo que las sesiones abiertas se cierran en la siguiente validación de la cookie.
/// <para>
/// Cada cambio administrativo se ejecuta en una sola transacción que primero toma el bloqueo de aplicación de las
/// cuentas (<see cref="ApplicationLocks.Accounts"/>). Así, la regla "siempre queda un administrador habilitado" se
/// evalúa sin carreras, los pasos de Identity (cuenta, rol y sello) se aplican todos o ninguno, y se comprueba que
/// quien actúa siga siendo un administrador habilitado (su cookie puede tener hasta un minuto de antigüedad).
/// </para>
/// </summary>
internal sealed class UserAccountService(
    UserManager<FixTrackUser> users,
    FixTrackDbContext db,
    IUnitOfWork unitOfWork,
    IAuditLog audit,
    TimeProvider clock) : IUserAccountService
{
    internal const string FullNameField = "FullName";
    internal const string EmailField = "Email";
    internal const string RoleField = "Role";
    internal const string PasswordField = "Password";
    internal const string ConfirmPasswordField = "ConfirmPassword";
    internal const string CurrentPasswordField = "CurrentPassword";

    internal const string BusyMessage = "Otra persona está modificando cuentas en este momento. Intenta de nuevo en unos segundos.";
    internal const string NotAdministratorMessage = "Tu cuenta ya no tiene permisos de administrador. Vuelve a iniciar sesión.";
    internal const string StaleMessage =
        "Otra persona modificó esta cuenta mientras la editabas. Revisa los datos actuales y vuelve a intentarlo.";

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
                select new
                {
                    user.Id,
                    user.FullName,
                    user.Email,
                    Role = role.Name,
                    user.LockoutEnd,
                    user.CreatedAtUtc,
                    user.LastSignInAtUtc,
                    user.ConcurrencyStamp,
                })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => ToAccount(
            row.Id, row.FullName, row.Email, row.Role, row.LockoutEnd, row.CreatedAtUtc, row.LastSignInAtUtc, row.ConcurrencyStamp, now))];
    }

    public async Task<UserAccount?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return null;
        }

        var role = (await users.GetRolesAsync(user)).FirstOrDefault();
        return ToAccount(
            user.Id, user.FullName, user.Email, role, user.LockoutEnd, user.CreatedAtUtc, user.LastSignInAtUtc, user.ConcurrencyStamp, clock.GetUtcNow());
    }

    public Task<CommandResult<string>> CreateAsync(NewUserAccountInput input, string actingUserId, CancellationToken cancellationToken) =>
        AsAdministratorAsync(actingUserId, token => CreateAccountAsync(input, AuditActions.AccountCreated, token), CommandResult<string>.Conflict, cancellationToken);

    public Task<CommandResult> UpdateAsync(string id, UserAccountInput input, string actingUserId, CancellationToken cancellationToken)
    {
        var fullName = Text.Clean(input.FullName);
        var errors = new Dictionary<string, string>();
        AddNameError(errors, fullName);
        AddRoleError(errors, input.Role);

        return AsAdministratorAsync(
            actingUserId,
            async token =>
            {
                var user = await users.FindByIdAsync(id);
                if (user is null)
                {
                    return CommandResult.NotFound();
                }

                if (errors.Count > 0)
                {
                    return CommandResult.Invalid(errors);
                }

                if (input.ConcurrencyStamp is null || input.ConcurrencyStamp != user.ConcurrencyStamp)
                {
                    return CommandResult.Conflict(StaleMessage);
                }

                var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault();
                var roleChanges = currentRole != input.Role;
                if (roleChanges && IsSelf(user, actingUserId))
                {
                    return CommandResult.Conflict("No puedes cambiar el rol de tu propia cuenta. Pídeselo a otro administrador.");
                }

                if (roleChanges && currentRole == Roles.Administrator && IsEnabled(user) && await CountEnabledAdministratorsAsync(token) <= 1)
                {
                    return CommandResult.Conflict("Es el único administrador habilitado: asigna antes el rol de administrador a otra cuenta.");
                }

                var changes = new List<string>();
                if (user.FullName != fullName)
                {
                    changes.Add($"nombre «{user.FullName}» → «{fullName}»");
                }

                if (roleChanges)
                {
                    changes.Add($"rol {currentRole ?? "sin rol"} → {input.Role}");
                }

                user.FullName = fullName;
                if (!roleChanges)
                {
                    return await AuditedAsync(Check(await users.UpdateAsync(user)), user, AuditActions.AccountEdited, changes, token);
                }

                // Quitar el rol anterior, agregar el nuevo y renovar el sello: todo dentro de la misma transacción.
                if (currentRole is not null && Check(await users.RemoveFromRoleAsync(user, currentRole)) is { Succeeded: false } removed)
                {
                    return removed;
                }

                if (Check(await users.AddToRoleAsync(user, input.Role!)) is { Succeeded: false } added)
                {
                    return added;
                }

                return await AuditedAsync(Check(await users.UpdateSecurityStampAsync(user)), user, AuditActions.AccountEdited, changes, token);
            },
            CommandResult.Conflict,
            cancellationToken);
    }

    public Task<CommandResult> SetEnabledAsync(string id, bool enabled, string actingUserId, CancellationToken cancellationToken) =>
        AsAdministratorAsync(
            actingUserId,
            async token =>
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
                    if (IsSelf(user, actingUserId))
                    {
                        return CommandResult.Conflict("No puedes deshabilitar tu propia cuenta.");
                    }

                    if (await users.IsInRoleAsync(user, Roles.Administrator) && await CountEnabledAdministratorsAsync(token) <= 1)
                    {
                        return CommandResult.Conflict("Es el único administrador habilitado: no puede deshabilitarse.");
                    }
                }

                // Un solo guardado: una cuenta deshabilitada queda bloqueada sin fecha de fin, se limpian los intentos
                // fallidos y el sello nuevo cierra sus sesiones. Sin pasos intermedios que puedan quedar a medias.
                user.LockoutEnabled = true;
                user.LockoutEnd = enabled ? null : DateTimeOffset.MaxValue;
                user.AccessFailedCount = 0;
                return await AuditedAsync(
                    Check(await users.UpdateSecurityStampAsync(user)),
                    user,
                    enabled ? AuditActions.AccountEnabled : AuditActions.AccountDisabled,
                    [enabled ? "habilitada" : "deshabilitada"],
                    token);
            },
            CommandResult.Conflict,
            cancellationToken);

    public Task<CommandResult> UnlockAsync(string id, string actingUserId, CancellationToken cancellationToken) =>
        AsAdministratorAsync(
            actingUserId,
            async token =>
            {
                var user = await users.FindByIdAsync(id);
                if (user is null)
                {
                    return CommandResult.NotFound();
                }

                if (!IsEnabled(user))
                {
                    return CommandResult.Conflict("La cuenta está deshabilitada: habilítala en lugar de desbloquearla.");
                }

                if (!IsLockedOut(user))
                {
                    return CommandResult.Conflict("La cuenta no está bloqueada.");
                }

                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
                return await AuditedAsync(Check(await users.UpdateAsync(user)), user, AuditActions.AccountUnlocked, ["desbloqueada"], token);
            },
            CommandResult.Conflict,
            cancellationToken);

    public Task<CommandResult> ResetPasswordAsync(
        string id,
        string? password,
        string? confirmPassword,
        string actingUserId,
        CancellationToken cancellationToken) =>
        AsAdministratorAsync(
            actingUserId,
            async token =>
            {
                var user = await users.FindByIdAsync(id);
                if (user is null)
                {
                    return CommandResult.NotFound();
                }

                // Sin pedir la contraseña actual, una sesión ajena abierta podría tomar la cuenta del administrador.
                if (IsSelf(user, actingUserId))
                {
                    return CommandResult.Conflict("Para cambiar tu propia contraseña usa «Cambiar contraseña» en el menú de tu cuenta.");
                }

                var errors = new Dictionary<string, string>();
                await AddPasswordErrorsAsync(errors, user, password, confirmPassword);
                if (errors.Count > 0)
                {
                    return CommandResult.Invalid(errors);
                }

                // El token de restablecimiento valida la contraseña nueva antes de reemplazar la anterior. Restablecer
                // también quita un bloqueo temporal; el sello nuevo cierra las sesiones abiertas.
                var resetToken = await users.GeneratePasswordResetTokenAsync(user);
                var reset = await users.ResetPasswordAsync(user, resetToken, password!);
                if (!reset.Succeeded)
                {
                    return CommandResult.Invalid(ToErrors(reset, PasswordField));
                }

                if (IsEnabled(user))
                {
                    user.LockoutEnd = null;
                }

                user.AccessFailedCount = 0;
                return await AuditedAsync(Check(await users.UpdateAsync(user)), user, AuditActions.PasswordReset, ["contraseña restablecida"], token);
            },
            CommandResult.Conflict,
            cancellationToken);

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
        // Comprobar y crear dentro del mismo bloqueo: dos ejecuciones simultáneas no pueden crear dos administradores.
        var outcome = InitialAdministratorOutcome.AlreadyExists;
        var result = await AsAdministratorAsync(
            actingUserId: null,
            async token =>
            {
                if (await CountEnabledAdministratorsAsync(token) > 0)
                {
                    return CommandResult<string>.Success(string.Empty);
                }

                outcome = InitialAdministratorOutcome.Created;
                return await CreateAccountAsync(
                    new NewUserAccountInput(fullName, email, Roles.Administrator, password, password), AuditActions.InitialAdministrator, token);
            },
            CommandResult<string>.Conflict,
            cancellationToken);

        return result.Status switch
        {
            CommandStatus.Succeeded => new InitialAdministratorResult(outcome, []),
            CommandStatus.Conflict => new InitialAdministratorResult(InitialAdministratorOutcome.Invalid, [result.Message ?? BusyMessage]),
            _ => new InitialAdministratorResult(InitialAdministratorOutcome.Invalid, [.. result.Errors.Values]),
        };
    }

    /// <summary>
    /// Ejecuta un cambio de cuentas en una transacción con el bloqueo de cuentas. Si <paramref name="actingUserId"/>
    /// no es nulo, primero comprueba que siga siendo un administrador habilitado. Un resultado no exitoso revierte todo.
    /// </summary>
    private Task<TResult> AsAdministratorAsync<TResult>(
        string? actingUserId,
        Func<CancellationToken, Task<TResult>> change,
        Func<string, TResult> conflict,
        CancellationToken cancellationToken)
        where TResult : CommandResult =>
        unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (!await db.Database.TryAcquireExclusiveAsync(ApplicationLocks.Accounts, ApplicationLocks.DefaultTimeout, token))
                {
                    return conflict(BusyMessage);
                }

                if (actingUserId is not null && !await IsEnabledAdministratorAsync(actingUserId, token))
                {
                    return conflict(NotAdministratorMessage);
                }

                return await change(token);
            },
            cancellationToken);

    /// <summary>Crea la cuenta y le asigna su rol; si algún paso falla, el resultado no exitoso revierte ambos.</summary>
    private async Task<CommandResult<string>> CreateAccountAsync(NewUserAccountInput input, string auditAction, CancellationToken cancellationToken)
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
        if (!added.Succeeded)
        {
            return CommandResult<string>.Invalid(ToErrors(added, RoleField));
        }

        await AuditedAsync(CommandResult.Success(), user, auditAction, [$"creada con rol {input.Role}"], cancellationToken);
        return CommandResult<string>.Success(user.Id);
    }

    /// <summary>
    /// Si el cambio salió bien, agrega el evento de auditoría y lo guarda en la misma transacción. Las operaciones de
    /// Identity ya guardaron sus cambios; este guardado solo agrega el evento.
    /// </summary>
    private async Task<CommandResult> AuditedAsync(
        CommandResult result,
        FixTrackUser user,
        string action,
        IReadOnlyList<string> changes,
        CancellationToken cancellationToken)
    {
        if (!result.Succeeded || changes.Count == 0)
        {
            return result;
        }

        audit.Record(action, AuditActions.Account, user.Id, $"Cuenta {user.Email}: {string.Join("; ", changes)}.");
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private Task<bool> IsEnabledAdministratorAsync(string userId, CancellationToken cancellationToken) =>
        EnabledAdministrators().AnyAsync(id => id == userId, cancellationToken);

    /// <summary>
    /// Administradores habilitados. Una cuenta deshabilitada tiene como fin de bloqueo <see cref="DateTimeOffset.MaxValue"/>;
    /// un bloqueo temporal por intentos fallidos no la deshabilita. Se compara por igualdad para que la consulta
    /// funcione igual en SQL Server y en SQLite.
    /// </summary>
    private Task<int> CountEnabledAdministratorsAsync(CancellationToken cancellationToken) =>
        EnabledAdministrators().CountAsync(cancellationToken);

    private IQueryable<string> EnabledAdministrators() =>
        from link in db.UserRoles
        join role in db.Roles on link.RoleId equals role.Id
        join user in db.Users on link.UserId equals user.Id
        where role.Name == Roles.Administrator && (user.LockoutEnd == null || user.LockoutEnd != DateTimeOffset.MaxValue)
        select user.Id;

    private static bool IsEnabled(FixTrackUser user) => user.LockoutEnd != DateTimeOffset.MaxValue;

    private bool IsLockedOut(FixTrackUser user) => IsEnabled(user) && user.LockoutEnd is { } until && until > clock.GetUtcNow();

    /// <summary>
    /// Compara con el Id guardado en la base, no con el de la ruta: SQL Server encuentra la cuenta aunque el GUID de la
    /// ruta esté en mayúsculas, y la regla debe aplicarse igual.
    /// </summary>
    private static bool IsSelf(FixTrackUser user, string actingUserId) => string.Equals(user.Id, actingUserId, StringComparison.Ordinal);

    /// <summary>Un fallo de Identity (por ejemplo, un cambio simultáneo de la cuenta) revierte la transacción.</summary>
    private static CommandResult Check(IdentityResult result) =>
        result.Succeeded
            ? CommandResult.Success()
            : CommandResult.Conflict(result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure))
                ? StaleMessage
                : result.Errors.First().Description);

    private static UserAccount ToAccount(
        string id,
        string fullName,
        string? email,
        string? role,
        DateTimeOffset? lockoutEnd,
        DateTime createdAtUtc,
        DateTime? lastSignInAtUtc,
        string? concurrencyStamp,
        DateTimeOffset now)
    {
        var disabled = lockoutEnd == DateTimeOffset.MaxValue;
        var lockedOut = !disabled && lockoutEnd is { } until && until > now;
        return new UserAccount(id, fullName, email ?? string.Empty, role, !disabled, lockedOut, createdAtUtc, lastSignInAtUtc, concurrencyStamp);
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
