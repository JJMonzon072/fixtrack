using System.Net;
using FixTrack.Application.Common;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Administración de cuentas de acceso: alta, validaciones, roles, estado y contraseñas.</summary>
public sealed class UsersPageTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> NewAccountForm(string email, string password, string role = Roles.Operator) => new()
    {
        ["FullName"] = "Rosa Recepción",
        ["Email"] = email,
        ["Role"] = role,
        ["Password"] = password,
        ["ConfirmPassword"] = password,
    };

    private async Task<string> AccountIdAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(Token);
    }

    private HttpClient FreshClient() => factory.CreateClient();

    [Fact]
    public async Task Administrators_list_accounts_with_their_roles_and_status()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var html = await client.GetStringAsync("/usuarios", Token);

        Assert.Contains(TestAccounts.Administrator.Email, html);
        Assert.Contains(TestAccounts.Operator.Email, html);
        Assert.Contains("Habilitada", html);
        Assert.Contains("Roles y permisos", html);
        Assert.Contains(">Tú</span>", html);
    }

    [Fact]
    public async Task Creates_an_operator_account_that_can_sign_in()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"rosa-{Guid.NewGuid():N}@fixtrack.test";
        var password = TestAccounts.RandomPassword();

        var created = await (await FormPost.SendAsync(admin, "/usuarios/nueva", NewAccountForm(email, password))).Content.ReadAsStringAsync(Token);
        using var client = FreshClient();
        using var login = await AuthenticatedClients.LogInAsync(client, email, password);
        var dashboard = await login.Content.ReadAsStringAsync(Token);

        Assert.Contains("Cuenta creada.", created);
        Assert.Contains(email, created);
        Assert.DoesNotContain(password, created);
        Assert.Equal("/", login.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Rosa Recepción", dashboard);
    }

    [Fact]
    public async Task Rejects_weak_passwords_mismatches_duplicates_and_missing_roles_without_echoing_passwords()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();

        var weak = await (await FormPost.SendAsync(admin, "/usuarios/nueva", NewAccountForm("debil@fixtrack.test", "corta"))).Content.ReadAsStringAsync(Token);
        var mismatch = NewAccountForm("distinta@fixtrack.test", "Valida123456789");
        mismatch["ConfirmPassword"] = "Otra123456789";
        var mismatchHtml = await (await FormPost.SendAsync(admin, "/usuarios/nueva", mismatch)).Content.ReadAsStringAsync(Token);
        var duplicate = await (await FormPost.SendAsync(admin, "/usuarios/nueva", NewAccountForm(TestAccounts.Operator.Email.ToUpperInvariant(), "Valida123456789"))).Content.ReadAsStringAsync(Token);
        var noRole = await (await FormPost.SendAsync(admin, "/usuarios/nueva", NewAccountForm("sinrol@fixtrack.test", "Valida123456789", role: "Superusuario"))).Content.ReadAsStringAsync(Token);

        Assert.Contains("La contraseña debe tener al menos 12 caracteres.", weak);
        Assert.Contains("La contraseña debe incluir al menos un número.", weak);
        Assert.Contains("Las contraseñas no coinciden.", mismatchHtml);
        Assert.Contains("Ya existe una cuenta con este correo electrónico.", duplicate);
        Assert.Contains("Selecciona el rol de la cuenta.", noRole);
        Assert.DoesNotContain("Valida123456789", mismatchHtml + duplicate + noRole);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Users.AnyAsync(u => u.Email == "sinrol@fixtrack.test", Token));
    }

    [Fact]
    public async Task Changes_the_role_of_another_account()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"cambio-{Guid.NewGuid():N}@fixtrack.test";
        await FormPost.SendAsync(admin, "/usuarios/nueva", NewAccountForm(email, TestAccounts.RandomPassword()));
        var id = await AccountIdAsync(email);

        var html = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/editar", new()
        {
            ["FullName"] = "Rosa Supervisora",
            ["Role"] = Roles.Administrator,
        })).Content.ReadAsStringAsync(Token);

        Assert.Contains("Los cambios se guardaron correctamente.", html);
        await using var scope = factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<IUserAccountService>().GetAsync(id, Token);
        Assert.Equal(("Rosa Supervisora", Roles.Administrator), (account!.FullName, account.Role));
    }

    [Fact]
    public async Task Administrators_cannot_disable_themselves_or_change_their_own_role()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var id = await AccountIdAsync(TestAccounts.Administrator.Email);

        var disable = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/deshabilitar", [])).Content.ReadAsStringAsync(Token);
        var demote = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/editar", new()
        {
            ["FullName"] = TestAccounts.Administrator.FullName,
            ["Role"] = Roles.Operator,
        })).Content.ReadAsStringAsync(Token);

        Assert.Contains("No puedes deshabilitar tu propia cuenta.", disable);
        Assert.Contains("No puedes cambiar el rol de tu propia cuenta.", demote);
        await using var scope = factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<IUserAccountService>().GetAsync(id, Token);
        Assert.Equal((true, Roles.Administrator), (account!.IsEnabled, account.Role));
    }

    [Fact]
    public async Task The_last_enabled_administrator_cannot_be_demoted_or_disabled()
    {
        await using var isolated = new FixTrackWebApplicationFactory();
        using var client = await isolated.CreateAuthenticatedClientAsync();
        await using var scope = isolated.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        var adminId = (await accounts.ListAsync(Token)).Single(a => a.Email == TestAccounts.Administrator.Email).Id;

        var demote = await accounts.UpdateAsync(adminId, new UserAccountInput(TestAccounts.Administrator.FullName, Roles.Operator), "otra-cuenta", Token);
        var disable = await accounts.SetEnabledAsync(adminId, enabled: false, actingUserId: "otra-cuenta", Token);

        Assert.Equal(CommandStatus.Conflict, demote.Status);
        Assert.Contains("único administrador", demote.Message);
        Assert.Equal(CommandStatus.Conflict, disable.Status);
    }

    [Fact]
    public async Task Disables_and_enables_another_account()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"estado-{Guid.NewGuid():N}@fixtrack.test";
        var password = TestAccounts.RandomPassword();
        await FormPost.SendAsync(admin, "/usuarios/nueva", NewAccountForm(email, password));
        var id = await AccountIdAsync(email);

        var disabled = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/deshabilitar", [])).Content.ReadAsStringAsync(Token);
        using var blocked = FreshClient();
        var blockedLogin = await (await AuthenticatedClients.LogInAsync(blocked, email, password)).Content.ReadAsStringAsync(Token);
        var enabled = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/habilitar", [])).Content.ReadAsStringAsync(Token);
        using var allowed = FreshClient();
        using var allowedLogin = await AuthenticatedClients.LogInAsync(allowed, email, password);

        Assert.Contains("Cuenta deshabilitada.", disabled);
        Assert.Contains("Deshabilitada", disabled);
        Assert.Contains(AccountController.LoginFailed, blockedLogin);
        Assert.Contains("Cuenta habilitada.", enabled);
        Assert.Equal("/", allowedLogin.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Resets_the_password_of_another_account()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"restablecer-{Guid.NewGuid():N}@fixtrack.test";
        await FormPost.SendAsync(admin, "/usuarios/nueva", NewAccountForm(email, TestAccounts.RandomPassword()));
        var id = await AccountIdAsync(email);
        var newPassword = TestAccounts.RandomPassword();

        var html = await (await FormPost.SendAsync(admin, $"/usuarios/{id}/contrasena", new()
        {
            ["Password"] = newPassword,
            ["ConfirmPassword"] = newPassword,
        })).Content.ReadAsStringAsync(Token);
        using var client = FreshClient();
        using var login = await AuthenticatedClients.LogInAsync(client, email, newPassword);

        Assert.Contains("Contraseña restablecida.", html);
        Assert.Equal("/", login.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Users_change_their_own_password_with_the_current_one()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"propia-{Guid.NewGuid():N}@fixtrack.test";
        var password = TestAccounts.RandomPassword();
        await FormPost.SendAsync(admin, "/usuarios/nueva", NewAccountForm(email, password));
        using var client = FreshClient();
        await AuthenticatedClients.LogInAsync(client, email, password);
        var newPassword = TestAccounts.RandomPassword();

        var wrong = await (await FormPost.SendAsync(client, "/cuenta/contrasena", new()
        {
            ["CurrentPassword"] = "NoEsLaActual123",
            ["Password"] = newPassword,
            ["ConfirmPassword"] = newPassword,
        })).Content.ReadAsStringAsync(Token);
        var changed = await (await FormPost.SendAsync(client, "/cuenta/contrasena", new()
        {
            ["CurrentPassword"] = password,
            ["Password"] = newPassword,
            ["ConfirmPassword"] = newPassword,
        })).Content.ReadAsStringAsync(Token);
        using var again = FreshClient();
        using var login = await AuthenticatedClients.LogInAsync(again, email, newPassword);

        Assert.Contains("La contraseña actual no es correcta.", wrong);
        Assert.Contains("Tu contraseña se cambió correctamente.", changed);
        Assert.Equal("/", login.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Unknown_accounts_return_not_found()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();

        using var response = await admin.GetAsync($"/usuarios/{Guid.NewGuid()}/editar", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
