using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FixTrack.Tests.Web;

/// <summary>Autenticación, autorización y protecciones de la aplicación, con el flujo real de inicio de sesión.</summary>
public sealed partial class SecurityTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HttpClient AnonymousClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Cuenta propia de la prueba, para no alterar las cuentas compartidas.</summary>
    private async Task<TestAccount> CreateAccountAsync(string role = Roles.Operator)
    {
        var account = new TestAccount("Cuenta De Prueba", $"prueba-{Guid.NewGuid():N}@fixtrack.test", role, TestAccounts.RandomPassword());
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<FixTrackUser>>();
        var user = new FixTrackUser { FullName = account.FullName, Email = account.Email, UserName = account.Email, CreatedAtUtc = DateTime.UtcNow };
        Assert.True((await users.CreateAsync(user, account.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return account;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/clientes")]
    [InlineData("/clientes/1")]
    [InlineData("/clientes/nuevo")]
    [InlineData("/equipos")]
    [InlineData("/equipos/1")]
    [InlineData("/tecnicos")]
    [InlineData("/ordenes")]
    [InlineData("/ordenes/1")]
    [InlineData("/cola-de-trabajo")]
    [InlineData("/usuarios")]
    [InlineData("/cuenta/contrasena")]
    public async Task Anonymous_visitors_are_sent_to_the_login_page(string url)
    {
        using var client = AnonymousClient();

        using var response = await client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/cuenta/iniciar-sesion?volver=", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task The_customer_suggestions_do_not_leak_data_without_a_session()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        await FormPost.CreateCustomerAsync(admin, "Cliente Confidencial", "5555-9100", "confidencial@example.com");
        using var client = AnonymousClient();

        using var json = new HttpRequestMessage(HttpMethod.Get, "/clientes/opciones?buscar=Confidencial");
        json.Headers.Accept.ParseAdd("application/json");
        using var jsonResponse = await client.SendAsync(json, Token);
        using var htmlResponse = await client.GetAsync("/clientes/opciones?buscar=Confidencial", Token);
        using var devices = await client.GetAsync("/clientes/1/equipos", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, jsonResponse.StatusCode);
        Assert.DoesNotContain("Confidencial", await jsonResponse.Content.ReadAsStringAsync(Token));
        Assert.Equal(HttpStatusCode.Redirect, htmlResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, devices.StatusCode);
    }

    [Fact]
    public async Task Anonymous_posts_cannot_create_records()
    {
        using var client = AnonymousClient();
        var page = await client.GetStringAsync("/cuenta/iniciar-sesion", Token);

        using var response = await client.PostAsync("/clientes/nuevo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Cliente Anonimo",
            ["Phone"] = "5555-9101",
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups["token"].Value,
        }), Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Customers.AnyAsync(c => c.FullName == "Cliente Anonimo", Token));
    }

    [Fact]
    public async Task Posts_without_an_antiforgery_token_are_rejected_even_with_a_session()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.PostAsync("/clientes/nuevo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Cliente Sin Token",
            ["Phone"] = "5555-9102",
        }), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/usuarios")]
    [InlineData("/usuarios/nueva")]
    [InlineData("/tecnicos/nuevo")]
    [InlineData("/tecnicos/1/editar")]
    public async Task Operators_cannot_open_administrator_pages(string url)
    {
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);

        using var response = await client.GetAsync(url, Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("No tienes permiso para esta acción", html);
    }

    [Fact]
    public async Task Operators_cannot_change_technicians_or_accounts()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var technicianId = FormPost.CreatedId(await FormPost.SendAsync(admin, "/tecnicos/nuevo", new()
        {
            ["FullName"] = "Tecnico Protegido",
            ["Specialty"] = "Redes",
        }));
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);

        using var create = await FormPost.SendAsync(client, "/tecnicos/nuevo", new() { ["FullName"] = "Tecnico Operador", ["Specialty"] = "Redes" });
        using var deactivate = await FormPost.SendAsync(client, $"/tecnicos/{technicianId}/desactivar", []);
        using var newAccount = await FormPost.SendAsync(client, "/usuarios/nueva", new()
        {
            ["FullName"] = "Cuenta Intrusa",
            ["Email"] = "intrusa@fixtrack.test",
            ["Role"] = Roles.Administrator,
            ["Password"] = "Intrusa1234567",
            ["ConfirmPassword"] = "Intrusa1234567",
        });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, newAccount.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
        Assert.True(await db.Technicians.AnyAsync(t => t.Id == technicianId && t.IsActive, Token));
        Assert.False(await db.Technicians.AnyAsync(t => t.FullName == "Tecnico Operador", Token));
        Assert.False(await db.Users.AnyAsync(u => u.Email == "intrusa@fixtrack.test", Token));
    }

    [Fact]
    public async Task Operators_run_the_workshop_but_do_not_see_administrator_actions()
    {
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);

        var customerId = await FormPost.CreateCustomerAsync(client, "Cliente Del Operador", "5555-9103");
        var technicians = await client.GetStringAsync("/tecnicos", Token);
        var orders = await client.GetAsync("/ordenes", Token);
        var dashboard = await client.GetStringAsync("/", Token);

        Assert.True(customerId > 0);
        Assert.Equal(HttpStatusCode.OK, orders.StatusCode);
        Assert.DoesNotContain("Nuevo técnico", technicians);
        Assert.DoesNotContain("href=\"/usuarios\"", dashboard);
        Assert.Contains("Oscar Operador", dashboard);
        Assert.Contains(Roles.Operator, dashboard);
    }

    [Fact]
    public async Task Administrators_see_the_accounts_menu()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var dashboard = await client.GetStringAsync("/", Token);

        Assert.Contains("href=\"/usuarios\"", dashboard);
        Assert.Contains("Cerrar sesión", dashboard);
    }

    [Fact]
    public async Task Wrong_credentials_get_the_same_generic_error_and_no_session()
    {
        using var unknown = AnonymousClient();
        using var wrong = AnonymousClient();

        var unknownHtml = await (await AuthenticatedClients.LogInAsync(unknown, "nadie@fixtrack.test", "Cualquier1234")).Content.ReadAsStringAsync(Token);
        using var wrongResponse = await AuthenticatedClients.LogInAsync(wrong, TestAccounts.Administrator.Email, "Incorrecta12345");
        var wrongHtml = await wrongResponse.Content.ReadAsStringAsync(Token);

        Assert.Contains(AccountController.LoginFailed, unknownHtml);
        Assert.Contains(AccountController.LoginFailed, wrongHtml);
        Assert.Equal(HttpStatusCode.OK, wrongResponse.StatusCode);
        Assert.DoesNotContain(wrongResponse.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], cookie => cookie.StartsWith(AuthenticatedClients.SessionCookie, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account_temporarily()
    {
        var account = await CreateAccountAsync();
        using var client = AnonymousClient();

        for (var attempt = 0; attempt < IdentitySettings.MaxFailedAttempts; attempt++)
        {
            await AuthenticatedClients.LogInAsync(client, account.Email, "Incorrecta12345");
        }

        var locked = await (await AuthenticatedClients.LogInAsync(client, account.Email, account.Password)).Content.ReadAsStringAsync(Token);

        // El mensaje es el genérico, pero la cuenta sí quedó bloqueada: ni la contraseña correcta abre sesión.
        Assert.Contains(AccountController.LoginFailed, locked);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<FixTrackUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByEmailAsync(account.Email))!));
    }

    [Fact]
    public async Task Logout_requires_post_and_ends_the_session()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await AuthenticatedClients.LogInAsync(client, account.Email, account.Password);

        using var viaGet = await client.GetAsync("/cuenta/cerrar-sesion", Token);
        var stillSignedIn = await client.GetAsync("/clientes", Token);
        var token = AntiforgeryToken().Match(await client.GetStringAsync("/clientes/nuevo", Token)).Groups["token"].Value;
        using var logout = await client.PostAsync("/cuenta/cerrar-sesion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }), Token);
        var afterLogout = await client.GetAsync("/clientes", Token);

        Assert.NotEqual(HttpStatusCode.OK, viaGet.StatusCode);
        Assert.Equal("/clientes", stillSignedIn.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Cerraste sesión correctamente.", await logout.Content.ReadAsStringAsync(Token));
        Assert.Equal("/cuenta/iniciar-sesion", afterLogout.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("/cuenta/registro")]
    [InlineData("/cuenta/registrarse")]
    [InlineData("/registro")]
    [InlineData("/Identity/Account/Register")]
    public async Task There_is_no_public_registration(string url)
    {
        using var anonymous = AnonymousClient();
        using var signedIn = await factory.CreateAuthenticatedClientAsync();

        using var anonymousResponse = await anonymous.GetAsync(url, Token);
        using var signedInResponse = await signedIn.GetAsync(url, Token);

        Assert.NotEqual(HttpStatusCode.OK, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, signedInResponse.StatusCode);
    }

    [Theory]
    [InlineData("https://sitio-malicioso.example/robar", "/")]
    [InlineData("//sitio-malicioso.example", "/")]
    [InlineData("/ordenes?vista=vencidas", "/ordenes?vista=vencidas")]
    public async Task After_login_only_local_return_addresses_are_followed(string returnUrl, string expected)
    {
        var account = await CreateAccountAsync();
        using var client = AnonymousClient();
        var page = await client.GetStringAsync("/cuenta/iniciar-sesion", Token);

        using var response = await client.PostAsync(
            $"/cuenta/iniciar-sesion?volver={Uri.EscapeDataString(returnUrl)}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = account.Email,
                ["Password"] = account.Password,
                ["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups["token"].Value,
            }),
            Token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task The_session_cookie_is_http_only_and_same_site()
    {
        var account = await CreateAccountAsync();
        using var client = AnonymousClient();

        using var response = await AuthenticatedClients.LogInAsync(client, account.Email, account.Password);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(AuthenticatedClients.SessionCookie, StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Outside_development_the_session_cookie_is_secure()
    {
        var account = await CreateAccountAsync();
        using var client = factory
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production").UseSetting("https_port", "443"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        using var response = await AuthenticatedClients.LogInAsync(client, account.Email, account.Password);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(AuthenticatedClients.SessionCookie, StringComparison.Ordinal));

        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pages_carry_security_headers_and_a_per_request_script_nonce()
    {
        using var client = AnonymousClient();

        using var first = await client.GetAsync("/cuenta/iniciar-sesion", Token);
        using var second = await client.GetAsync("/cuenta/iniciar-sesion", Token);
        var html = await first.Content.ReadAsStringAsync(Token);
        var policy = first.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = Regex.Match(policy, "'nonce-(?<value>[^']+)'").Groups["value"].Value;

        Assert.Matches("^[A-Za-z0-9_-]{22}$", nonce);
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.DoesNotContain("unsafe-inline", policy);
        Assert.Contains($"<script nonce=\"{nonce}\">", html);
        Assert.NotEqual(policy, second.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", first.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", first.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", first.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Login_attempts_are_limited_per_address()
    {
        using var client = factory
            .WithWebHostBuilder(builder => builder.UseSetting("Security:LoginAttemptsPerMinute", "3"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await AuthenticatedClients.LogInAsync(client, "nadie@fixtrack.test", "Cualquier1234");
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task A_disabled_account_loses_its_open_session_and_cannot_sign_in_again()
    {
        var account = await CreateAccountAsync();

        // Validación del sello de seguridad en cada solicitud (en producción, cada minuto).
        var immediate = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));
        using var session = immediate.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await AuthenticatedClients.LogInAsync(session, account.Email, account.Password);
        using var before = await session.GetAsync("/clientes", Token);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            var id = (await accounts.ListAsync(Token)).Single(a => a.Email == account.Email).Id;
            Assert.True((await accounts.SetEnabledAsync(id, enabled: false, actingUserId: await AdministratorIdAsync(), Token)).Succeeded);
        }

        using var after = await session.GetAsync("/clientes", Token);
        using var again = immediate.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var retry = await (await AuthenticatedClients.LogInAsync(again, account.Email, account.Password)).Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Contains(AccountController.LoginFailed, retry);
    }

    [Fact]
    public async Task Passwords_never_appear_in_logs_or_pages()
    {
        var logs = new CapturingLoggerProvider();
        var account = await CreateAccountAsync();
        var newPassword = TestAccounts.RandomPassword();
        var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = logged.CreateClient();

        var failed = await (await AuthenticatedClients.LogInAsync(client, account.Email, "Incorrecta98765")).Content.ReadAsStringAsync(Token);
        var signedIn = await (await AuthenticatedClients.LogInAsync(client, account.Email, account.Password)).Content.ReadAsStringAsync(Token);
        var changed = await (await FormPost.SendAsync(client, "/cuenta/contrasena", new()
        {
            ["CurrentPassword"] = account.Password,
            ["Password"] = newPassword,
            ["ConfirmPassword"] = "no-coincide",
        })).Content.ReadAsStringAsync(Token);

        foreach (var secret in new[] { account.Password, newPassword, "Incorrecta98765" })
        {
            Assert.DoesNotContain(logs.Messages, message => message.Contains(secret, StringComparison.Ordinal));
            Assert.DoesNotContain(secret, failed);
            Assert.DoesNotContain(secret, signedIn);
            Assert.DoesNotContain(secret, changed);
        }

        Assert.Contains("Las contraseñas no coinciden.", changed);
        Assert.NotEmpty(logs.Messages);
    }

    [Fact]
    public void The_configuration_files_in_the_repository_contain_no_credentials()
    {
        var web = Path.Combine(RepositoryRoot(), "src", "FixTrack.Web");
        var files = Directory.GetFiles(web, "appsettings*.json");

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (var forbidden in new[] { "Password", "Pwd", "User ID", "ConnectionStrings", "InitialAdmin" })
            {
                Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FixTrack.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception);
        }
    }
}
