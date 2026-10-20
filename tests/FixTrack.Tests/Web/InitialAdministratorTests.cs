using FixTrack.Application.Security;
using FixTrack.Infrastructure.Identity;
using FixTrack.Web.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Mecanismo explícito para crear el primer administrador (comando <c>crear-administrador</c>).</summary>
public sealed class InitialAdministratorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(new[] { "crear-administrador" }, true)]
    [InlineData(new[] { "aplicar-migraciones" }, true)]
    [InlineData(new[] { "--urls", "http://localhost:5104" }, false)]
    [InlineData(new string[0], false)]
    public void Only_the_explicit_commands_replace_the_web_server(string[] args, bool expected) =>
        Assert.Equal(expected, ConsoleCommands.IsCommand(args));

    [Fact]
    public async Task Does_nothing_while_an_enabled_administrator_exists()
    {
        await using var factory = new FixTrackWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        var result = await accounts.CreateInitialAdministratorAsync("toma@fixtrack.test", "Toma De Control", TestAccounts.RandomPassword(), Token);

        Assert.Equal(InitialAdministratorOutcome.AlreadyExists, result.Outcome);
        Assert.DoesNotContain(await accounts.ListAsync(Token), account => account.Email == "toma@fixtrack.test");
    }

    [Fact]
    public async Task Creates_the_first_administrator_when_none_is_enabled_and_validates_the_password()
    {
        await using var factory = new FixTrackWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<FixTrackUser>>();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();

        // Instalación sin administradores habilitados: la cuenta de prueba se deshabilita directamente.
        var seeded = await users.FindByEmailAsync(TestAccounts.Administrator.Email);
        await users.SetLockoutEndDateAsync(seeded!, DateTimeOffset.MaxValue);

        var weak = await accounts.CreateInitialAdministratorAsync("primera@fixtrack.test", "Primera Administradora", "debil", Token);
        var created = await accounts.CreateInitialAdministratorAsync(" Primera@FixTrack.test ", "Primera Administradora", TestAccounts.RandomPassword(), Token);
        var second = await accounts.CreateInitialAdministratorAsync("segunda@fixtrack.test", "Segunda Administradora", TestAccounts.RandomPassword(), Token);

        Assert.Equal(InitialAdministratorOutcome.Invalid, weak.Outcome);
        Assert.Contains(weak.Errors, error => error.Contains("12 caracteres", StringComparison.Ordinal));
        Assert.Equal(InitialAdministratorOutcome.Created, created.Outcome);
        Assert.Equal(InitialAdministratorOutcome.AlreadyExists, second.Outcome);
        var administrator = (await accounts.ListAsync(Token)).Single(account => account.Email == "primera@fixtrack.test");
        Assert.Equal((Roles.Administrator, true), (administrator.Role, administrator.IsEnabled));
    }
}
