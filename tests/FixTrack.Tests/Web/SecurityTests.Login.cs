using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Identity;
using FixTrack.Web.Controllers;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FixTrack.Tests.Web;

/// <summary>El inicio de sesión no revela si un correo tiene cuenta ni en qué estado está.</summary>
public sealed partial class SecurityTests
{
    private const string WrongPassword = "Incorrecta12345";

    /// <summary>Correo sin cuenta, contraseña incorrecta, cuenta bloqueada y cuenta deshabilitada.</summary>
    private async Task<Dictionary<string, (TestAccount Account, string Password)>> FailureCasesAsync()
    {
        var wrong = await CreateAccountAsync();
        var locked = await CreateAccountAsync();
        var disabled = await CreateAccountAsync();
        using (var client = AnonymousClient())
        {
            for (var attempt = 0; attempt < IdentitySettings.MaxFailedAttempts; attempt++)
            {
                await AuthenticatedClients.LogInAsync(client, locked.Email, WrongPassword);
            }
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            var id = (await accounts.ListAsync(Token)).Single(a => a.Email == disabled.Email).Id;
            Assert.True((await accounts.SetEnabledAsync(id, enabled: false, actingUserId: await AdministratorIdAsync(), Token)).Succeeded);
        }

        var unknown = new TestAccount("Nadie", $"nadie-{Guid.NewGuid():N}@fixtrack.test", Roles.Operator, TestAccounts.RandomPassword());
        return new()
        {
            ["correo sin cuenta"] = (unknown, unknown.Password),
            ["contraseña incorrecta"] = (wrong, WrongPassword),
            // Con la contraseña correcta: aun así no debe distinguirse de un error de credenciales.
            ["cuenta bloqueada"] = (locked, locked.Password),
            ["cuenta deshabilitada"] = (disabled, disabled.Password),
        };
    }

    private async Task<string> AdministratorIdAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        return (await accounts.ListAsync(Token)).Single(a => a.Email == TestAccounts.Administrator.Email).Id;
    }

    /// <summary>Quita lo que cambia en cada solicitud: el token antifalsificación, el nonce y el correo escrito.</summary>
    private static string Normalize(string html, string email) =>
        VolatileAttribute().Replace(html, "$1\"…\"").Replace(email, "…", StringComparison.Ordinal);

    [Fact]
    public async Task Every_failed_login_gets_an_identical_response()
    {
        var cases = await FailureCasesAsync();
        var responses = new Dictionary<string, (HttpStatusCode Status, string Html, bool Session)>();

        foreach (var (name, (account, password)) in cases)
        {
            using var client = AnonymousClient();
            using var response = await AuthenticatedClients.LogInAsync(client, account.Email, password);
            var html = await response.Content.ReadAsStringAsync(Token);
            var session = response.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(cookie => cookie.StartsWith(AuthenticatedClients.SessionCookie, StringComparison.Ordinal));
            responses[name] = (response.StatusCode, Normalize(html, account.Email), session);
        }

        var reference = responses["correo sin cuenta"];
        Assert.Equal(HttpStatusCode.OK, reference.Status);
        Assert.Contains(AccountController.LoginFailed, reference.Html);
        foreach (var (name, response) in responses)
        {
            Assert.False(response.Session, $"El caso «{name}» abrió una sesión.");
            Assert.Equal(reference.Status, response.Status);
            Assert.True(reference.Html == response.Html, $"La respuesta del caso «{name}» es distinta de la de un correo sin cuenta.");
            Assert.DoesNotContain("bloquead", response.Html.Replace(AccountController.LoginFailed, string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("deshabilitad", response.Html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Unknown_and_locked_accounts_still_compute_a_password_hash()
    {
        var counter = new HashCounter();
        var counted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IPasswordHasher<FixTrackUser>>(provider => new CountingPasswordHasher(
                new PasswordHasher<FixTrackUser>(provider.GetRequiredService<IOptions<PasswordHasherOptions>>()), counter))));
        var locked = await CreateAccountAsync();
        using var client = counted.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        for (var attempt = 0; attempt < IdentitySettings.MaxFailedAttempts; attempt++)
        {
            await AuthenticatedClients.LogInAsync(client, locked.Email, WrongPassword);
        }

        var beforeUnknown = counter.Verifications;
        await AuthenticatedClients.LogInAsync(client, $"nadie-{Guid.NewGuid():N}@fixtrack.test", WrongPassword);
        var unknown = counter.Verifications - beforeUnknown;
        var beforeLocked = counter.Verifications;
        await AuthenticatedClients.LogInAsync(client, locked.Email, locked.Password);
        var whileLocked = counter.Verifications - beforeLocked;

        Assert.Equal(1, unknown);
        Assert.Equal(1, whileLocked);
    }

    [Fact]
    public async Task Failed_logins_take_at_least_the_minimum_duration()
    {
        var slow = factory.WithWebHostBuilder(builder => builder.UseSetting(FailedLoginEqualizer.MinimumDurationSetting, "300"));
        var account = await CreateAccountAsync();
        using var client = slow.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        async Task<TimeSpan> TimeAsync(string email, string password)
        {
            var clock = Stopwatch.StartNew();
            using var response = await AuthenticatedClients.LogInAsync(client, email, password);
            return clock.Elapsed;
        }

        // El tiempo incluye también la carga previa del formulario; lo que importa es el mínimo.
        Assert.True(await TimeAsync($"nadie-{Guid.NewGuid():N}@fixtrack.test", WrongPassword) >= TimeSpan.FromMilliseconds(300));
        Assert.True(await TimeAsync(account.Email, WrongPassword) >= TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task The_log_keeps_the_real_reason_without_emails_or_passwords()
    {
        var logs = new CapturingLoggerProvider();
        var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        var cases = await FailureCasesAsync();

        foreach (var (account, password) in cases.Values)
        {
            using var client = logged.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await AuthenticatedClients.LogInAsync(client, account.Email, password);
        }

        Assert.Contains(logs.Messages, message => message.Contains("el correo no tiene cuenta", StringComparison.Ordinal));
        Assert.Contains(logs.Messages, message => message.Contains("contraseña incorrecta para la cuenta", StringComparison.Ordinal));
        Assert.Contains(logs.Messages, message => message.Contains("está bloqueada temporalmente", StringComparison.Ordinal));
        Assert.Contains(logs.Messages, message => message.Contains("está deshabilitada", StringComparison.Ordinal));
        foreach (var (account, password) in cases.Values)
        {
            Assert.DoesNotContain(logs.Messages, message => message.Contains(account.Email, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(logs.Messages, message => message.Contains(password, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Guessing_the_current_password_from_an_open_session_locks_the_account()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        await AuthenticatedClients.LogInAsync(client, account.Email, account.Password);
        var newPassword = TestAccounts.RandomPassword();

        for (var attempt = 0; attempt < IdentitySettings.MaxFailedAttempts; attempt++)
        {
            await FormPost.SendAsync(client, "/cuenta/contrasena", new()
            {
                ["CurrentPassword"] = WrongPassword,
                ["Password"] = newPassword,
                ["ConfirmPassword"] = newPassword,
            });
        }

        var blocked = await (await FormPost.SendAsync(client, "/cuenta/contrasena", new()
        {
            ["CurrentPassword"] = account.Password,
            ["Password"] = newPassword,
            ["ConfirmPassword"] = newPassword,
        })).Content.ReadAsStringAsync(Token);
        using var other = AnonymousClient();
        using var login = await AuthenticatedClients.LogInAsync(other, account.Email, account.Password);

        Assert.Contains("el cambio de contraseña está bloqueado unos minutos", blocked);
        Assert.Contains(AccountController.LoginFailed, await login.Content.ReadAsStringAsync(Token));
    }

    [GeneratedRegex("((?:value|nonce))=\"[^\"]*\"")]
    private static partial Regex VolatileAttribute();

    private sealed class HashCounter
    {
        private int _verifications;

        public int Verifications => Volatile.Read(ref _verifications);

        public void Count() => Interlocked.Increment(ref _verifications);
    }

    private sealed class CountingPasswordHasher(IPasswordHasher<FixTrackUser> inner, HashCounter counter) : IPasswordHasher<FixTrackUser>
    {
        public string HashPassword(FixTrackUser user, string password) => inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(FixTrackUser user, string hashedPassword, string providedPassword)
        {
            counter.Count();
            return inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
