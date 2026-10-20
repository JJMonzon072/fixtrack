using FixTrack.Application.Common;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Controllers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>
/// Reglas de la administración de cuentas: cambios atómicos, quien actúa debe seguir siendo administrador, desbloqueo,
/// restablecimiento de contraseñas y formularios desactualizados. La concurrencia real se prueba en SQL Server.
/// </summary>
public sealed class AccountAdministrationTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<string> IdAsync(IUserAccountService accounts, string email) =>
        (await accounts.ListAsync(Token)).Single(account => account.Email == email).Id;

    private static NewUserAccountInput NewAccount(string email, string role = Roles.Operator)
    {
        var password = TestAccounts.RandomPassword();
        return new NewUserAccountInput("Cuenta Administrada", email, role, password, password);
    }

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@fixtrack.test";

    [Fact]
    public async Task A_role_change_that_fails_halfway_leaves_the_account_unchanged()
    {
        var failure = new ArmedFailure();
        await using var isolated = WithFailingValidator(failure);
        await using var scope = isolated.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var admin = await IdAsync(accounts, TestAccounts.Administrator.Email);
        var id = (await accounts.CreateAsync(NewAccount(NewEmail("atomico")), admin, Token)).Value!;
        var before = (await accounts.GetAsync(id, Token))!;

        // Quitar el rol anterior se guarda; agregar el nuevo falla: todo debe revertirse.
        failure.FailOnCall(2);
        var result = await accounts.UpdateAsync(id, new UserAccountInput("Nombre Cambiado", Roles.Administrator, before.ConcurrencyStamp), admin, Token);

        await using var check = isolated.Services.CreateAsyncScope();
        var after = (await check.ServiceProvider.GetRequiredService<IUserAccountService>().GetAsync(id, Token))!;
        var roles = await check.ServiceProvider.GetRequiredService<FixTrackDbContext>().UserRoles.CountAsync(link => link.UserId == id, Token);
        Assert.Equal(CommandStatus.Conflict, result.Status);
        Assert.Equal((before.FullName, Roles.Operator, before.ConcurrencyStamp), (after.FullName, after.Role, after.ConcurrencyStamp));
        Assert.Equal(1, roles);
    }

    [Fact]
    public async Task An_account_is_never_created_without_its_role()
    {
        var failure = new ArmedFailure();
        await using var isolated = WithFailingValidator(failure);
        await using var scope = isolated.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var admin = await IdAsync(accounts, TestAccounts.Administrator.Email);
        var email = NewEmail("sin-rol");

        // Crear la cuenta se guarda; asignarle el rol falla: la cuenta tampoco debe quedar.
        failure.FailOnCall(2);
        var result = await accounts.CreateAsync(NewAccount(email), admin, Token);

        Assert.False(result.Succeeded);
        await using var check = isolated.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<FixTrackDbContext>().Users.AnyAsync(user => user.Email == email, Token));
    }

    [Fact]
    public async Task An_administrator_who_lost_the_role_cannot_keep_managing_accounts()
    {
        await using var isolated = new FixTrackWebApplicationFactory();
        await using var scope = isolated.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var first = await IdAsync(accounts, TestAccounts.Administrator.Email);
        var second = (await accounts.CreateAsync(NewAccount(NewEmail("segundo"), Roles.Administrator), first, Token)).Value!;
        var stamp = (await accounts.GetAsync(second, Token))!.ConcurrencyStamp;

        Assert.True((await accounts.UpdateAsync(second, new UserAccountInput("Cuenta Administrada", Roles.Operator, stamp), first, Token)).Succeeded);

        // Con una cookie emitida antes del cambio, la segunda cuenta todavía "parece" administradora.
        var disable = await accounts.SetEnabledAsync(first, enabled: false, second, Token);
        var create = await accounts.CreateAsync(NewAccount(NewEmail("intruso"), Roles.Administrator), second, Token);
        var reset = await accounts.ResetPasswordAsync(first, "Nueva123456789", "Nueva123456789", second, Token);

        Assert.All(new CommandResult[] { disable, create, reset }, result => Assert.Equal(CommandStatus.Conflict, result.Status));
        Assert.Contains("ya no tiene permisos de administrador", disable.Message);
        var firstAccount = (await accounts.GetAsync(first, Token))!;
        Assert.Equal((true, Roles.Administrator), (firstAccount.IsEnabled, firstAccount.Role));
    }

    [Fact]
    public async Task Two_administrators_disabling_each_other_leave_one_enabled()
    {
        await using var isolated = new FixTrackWebApplicationFactory();
        await using var scope = isolated.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var first = await IdAsync(accounts, TestAccounts.Administrator.Email);
        var second = (await accounts.CreateAsync(NewAccount(NewEmail("segundo"), Roles.Administrator), first, Token)).Value!;

        var firstDisablesSecond = await accounts.SetEnabledAsync(second, enabled: false, first, Token);
        var secondDisablesFirst = await accounts.SetEnabledAsync(first, enabled: false, second, Token);

        Assert.True(firstDisablesSecond.Succeeded);
        Assert.Equal(CommandStatus.Conflict, secondDisablesFirst.Status);
        Assert.Equal(1, (await accounts.ListAsync(Token)).Count(account => account is { Role: Roles.Administrator, IsEnabled: true }));
    }

    [Fact]
    public async Task Disabling_an_account_blocks_it_and_renews_its_stamp_in_one_change()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<FixTrackUser>>();
        var admin = await IdAsync(accounts, TestAccounts.Administrator.Email);
        var id = (await accounts.CreateAsync(NewAccount(NewEmail("deshabilitar")), admin, Token)).Value!;
        var before = (await users.FindByIdAsync(id))!.SecurityStamp;

        Assert.True((await accounts.SetEnabledAsync(id, enabled: false, admin, Token)).Succeeded);

        await using var check = factory.Services.CreateAsyncScope();
        var user = (await check.ServiceProvider.GetRequiredService<UserManager<FixTrackUser>>().FindByIdAsync(id))!;
        Assert.Equal((DateTimeOffset.MaxValue, true, 0), (user.LockoutEnd!.Value, user.LockoutEnabled, user.AccessFailedCount));
        Assert.NotEqual(before, user.SecurityStamp);
    }

    [Fact]
    public async Task Administrators_unlock_a_temporarily_locked_account()
    {
        var email = NewEmail("bloqueada");
        string id, password;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            var input = NewAccount(email);
            password = input.Password!;
            id = (await accounts.CreateAsync(input, await IdAsync(accounts, TestAccounts.Administrator.Email), Token)).Value!;
        }

        using (var attacker = factory.CreateClient())
        {
            for (var attempt = 0; attempt < IdentitySettings.MaxFailedAttempts; attempt++)
            {
                await AuthenticatedClients.LogInAsync(attacker, email, "Incorrecta12345");
            }
        }

        using var admin = await factory.CreateAuthenticatedClientAsync();
        var editor = await admin.GetStringAsync($"/usuarios/{id}/editar", Token);
        var unlocked = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/desbloquear", [])).Content.ReadAsStringAsync(Token);
        var again = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/desbloquear", [])).Content.ReadAsStringAsync(Token);
        using var user = factory.CreateClient();
        using var login = await AuthenticatedClients.LogInAsync(user, email, password);

        Assert.Contains("Bloqueada temporalmente", editor);
        Assert.Contains("Desbloquear", editor);
        Assert.Contains("Cuenta desbloqueada.", unlocked);
        Assert.Contains("La cuenta no está bloqueada.", again);
        Assert.Equal("/", login.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Resetting_a_password_also_removes_a_temporary_lockout()
    {
        var email = NewEmail("restablecer");
        string id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            id = (await accounts.CreateAsync(NewAccount(email), await IdAsync(accounts, TestAccounts.Administrator.Email), Token)).Value!;
        }

        using (var attacker = factory.CreateClient())
        {
            for (var attempt = 0; attempt < IdentitySettings.MaxFailedAttempts; attempt++)
            {
                await AuthenticatedClients.LogInAsync(attacker, email, "Incorrecta12345");
            }
        }

        using var admin = await factory.CreateAuthenticatedClientAsync();
        var newPassword = TestAccounts.RandomPassword();
        await FormPost.SendAsync(admin, $"/usuarios/{id}/contrasena", new() { ["Password"] = newPassword, ["ConfirmPassword"] = newPassword });
        using var user = factory.CreateClient();
        using var login = await AuthenticatedClients.LogInAsync(user, email, newPassword);

        Assert.Equal("/", login.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Administrators_change_their_own_password_only_with_the_current_one()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        string id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            id = await IdAsync(scope.ServiceProvider.GetRequiredService<IUserAccountService>(), TestAccounts.Administrator.Email);
        }

        var editor = await admin.GetStringAsync($"/usuarios/{id}/editar", Token);
        var newPassword = TestAccounts.RandomPassword();
        var reset = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/contrasena", new()
        {
            ["Password"] = newPassword,
            ["ConfirmPassword"] = newPassword,
        })).Content.ReadAsStringAsync(Token);

        Assert.Contains("Cambiar mi contraseña", editor);
        Assert.DoesNotContain("Restablecer contraseña", editor);
        Assert.Contains("usa «Cambiar contraseña»", reset);
        using var other = factory.CreateClient();
        using var login = await AuthenticatedClients.LogInAsync(other, TestAccounts.Administrator.Email, TestAccounts.Administrator.Password);
        Assert.Equal("/", login.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task A_stale_edit_form_does_not_overwrite_a_newer_change()
    {
        string id, admin;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            admin = await IdAsync(accounts, TestAccounts.Administrator.Email);
            id = (await accounts.CreateAsync(NewAccount(NewEmail("editar")), admin, Token)).Value!;
        }

        using var client = await factory.CreateAuthenticatedClientAsync();
        var page = await client.GetStringAsync($"/usuarios/{id}/editar", Token);
        var staleStamp = System.Text.RegularExpressions.Regex.Match(page, "name=\"ConcurrencyStamp\" value=\"(?<stamp>[^\"]+)\"").Groups["stamp"].Value;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            Assert.True((await accounts.UpdateAsync(id, new UserAccountInput("Nombre Más Reciente", Roles.Operator, staleStamp), admin, Token)).Succeeded);
        }

        var html = await (await FormPost.SendAsync(client, $"/usuarios/{id}/editar", new()
        {
            ["FullName"] = "Nombre Desactualizado",
            ["Role"] = Roles.Operator,
            ["ConcurrencyStamp"] = staleStamp,
        })).Content.ReadAsStringAsync(Token);

        Assert.NotEmpty(staleStamp);
        Assert.Contains("Otra persona modificó esta cuenta", html);
        await using var check = factory.Services.CreateAsyncScope();
        Assert.Equal("Nombre Más Reciente", (await check.ServiceProvider.GetRequiredService<IUserAccountService>().GetAsync(id, Token))!.FullName);
    }

    [Fact]
    public async Task An_operator_cannot_unlock_or_reset_accounts_with_manual_requests()
    {
        using var operatorClient = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);
        string id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            id = await IdAsync(scope.ServiceProvider.GetRequiredService<IUserAccountService>(), TestAccounts.Administrator.Email);
        }

        using var unlock = await FormPost.SendAsync(operatorClient, $"/usuarios/{id}/desbloquear", []);
        using var reset = await FormPost.SendAsync(operatorClient, $"/usuarios/{id}/contrasena", new()
        {
            ["Password"] = "Nueva123456789",
            ["ConfirmPassword"] = "Nueva123456789",
        });

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, unlock.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, reset.StatusCode);
    }

    [Fact]
    public async Task Account_changes_are_audited_with_their_author_and_without_passwords()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var email = NewEmail("auditada");
        var initial = TestAccounts.RandomPassword();
        await FormPost.SendAsync(admin, "/usuarios/nueva", new()
        {
            ["FullName"] = "Rosa Auditada",
            ["Email"] = email,
            ["Role"] = Roles.Operator,
            ["Password"] = initial,
            ["ConfirmPassword"] = initial,
        });
        string id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            id = await IdAsync(scope.ServiceProvider.GetRequiredService<IUserAccountService>(), email);
        }

        var reset = TestAccounts.RandomPassword();
        await FormPost.SendAsync(admin, $"/usuarios/{id}/contrasena", new() { ["Password"] = reset, ["ConfirmPassword"] = reset });
        await FormPost.SendAsync(admin, $"/usuarios/{id}/deshabilitar", []);

        await using var check = factory.Services.CreateAsyncScope();
        var events = await check.ServiceProvider.GetRequiredService<FixTrackDbContext>().AuditEvents
            .Where(audit => audit.EntityType == "cuenta" && audit.EntityId == id)
            .OrderBy(audit => audit.Id)
            .ToListAsync(Token);
        Assert.Equal(["cuenta.creada", "cuenta.contrasena-restablecida", "cuenta.deshabilitada"], events.Select(audit => audit.Action));
        Assert.All(events, audit =>
        {
            Assert.Equal(TestAccounts.Administrator.FullName, audit.UserName);
            Assert.Contains(email, audit.Summary);
            Assert.DoesNotContain(initial, audit.Summary);
            Assert.DoesNotContain(reset, audit.Summary);
        });
        Assert.Contains("creada con rol Operador", events[0].Summary);
    }

    private static WebApplicationFactory<Program> WithFailingValidator(ArmedFailure failure) =>
        new FixTrackWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IUserValidator<FixTrackUser>>(_ => new FailingUserValidator(failure))));

    /// <summary>Hace fallar la validación número N después de activarse; antes no interviene.</summary>
    private sealed class ArmedFailure
    {
        private int _failOn;
        private int _calls;

        public void FailOnCall(int call)
        {
            _calls = 0;
            _failOn = call;
        }

        public bool ShouldFail() => _failOn > 0 && Interlocked.Increment(ref _calls) == _failOn;
    }

    private sealed class FailingUserValidator(ArmedFailure failure) : IUserValidator<FixTrackUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<FixTrackUser> manager, FixTrackUser user) =>
            Task.FromResult(failure.ShouldFail()
                ? IdentityResult.Failed(new IdentityError { Code = "FalloSimulado", Description = "Fallo simulado en medio del cambio." })
                : IdentityResult.Success);
    }
}
