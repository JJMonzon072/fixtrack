using System.Diagnostics;
using FixTrack.Application.Common;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.SqlServer;

/// <summary>
/// Concurrencia real en la administración de cuentas: solicitudes simultáneas, cada una con su propia conexión, contra
/// la base desechable de <see cref="DisposableSqlServerDatabase"/>. Estas pruebas confirman transacciones, así que
/// nunca se ejecutan contra la base indicada en FIXTRACK_TEST_SQLSERVER.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AccountConcurrencyTests(DisposableSqlServerDatabase database) : IAsyncLifetime
{
    private const int Rounds = 5;

    private WebApplicationFactory<Program>? _app;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> App =>
        _app ?? throw new InvalidOperationException("La base desechable no está configurada.");

    public async ValueTask InitializeAsync()
    {
        if (database.ConnectionString is not { } connectionString)
        {
            return;
        }

        _app = await DisposableApp.CreateAsync(connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private void SkipUnlessDisposable() =>
        Assert.SkipUnless(database.ConnectionString is not null, $"Define {DisposableSqlServerDatabase.ServerVariable} para ejecutar estas pruebas.");

    private async Task<T> InScopeAsync<T>(Func<IUserAccountService, Task<T>> action)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IUserAccountService>());
    }

    private static NewUserAccountInput Administrator(string prefix)
    {
        var password = $"Pr{Guid.NewGuid():N}9z".Insert(10, "Q");
        return new NewUserAccountInput("Administrador De Prueba", $"{prefix}-{Guid.NewGuid():N}@fixtrack.test", Roles.Administrator, password, password);
    }

    /// <summary>Deja exactamente dos administradores habilitados, A y B, y devuelve sus Id.</summary>
    private async Task<(string A, string B)> TwoAdministratorsOnlyAsync()
    {
        var current = await InScopeAsync(async accounts =>
        {
            var enabled = (await accounts.ListAsync(Token)).Where(account => account is { Role: Roles.Administrator, IsEnabled: true }).ToList();
            if (enabled.Count > 0)
            {
                return enabled;
            }

            var initial = Administrator("inicial");
            await accounts.CreateInitialAdministratorAsync(initial.Email, initial.FullName, initial.Password, Token);
            return (await accounts.ListAsync(Token)).Where(account => account is { Role: Roles.Administrator, IsEnabled: true }).ToList();
        });

        var actor = current[0].Id;
        var a = (await InScopeAsync(accounts => accounts.CreateAsync(Administrator("a"), actor, Token))).Value!;
        var b = (await InScopeAsync(accounts => accounts.CreateAsync(Administrator("b"), actor, Token))).Value!;
        foreach (var other in current)
        {
            Assert.True((await InScopeAsync(accounts => accounts.SetEnabledAsync(other.Id, enabled: false, a, Token))).Succeeded);
        }

        return (a, b);
    }

    private Task<int> EnabledAdministratorsAsync() =>
        InScopeAsync(async accounts => (await accounts.ListAsync(Token)).Count(account => account is { Role: Roles.Administrator, IsEnabled: true }));

    /// <summary>Ejecuta las dos operaciones a la vez, cada una en su propio ámbito y conexión.</summary>
    private static async Task<CommandResult[]> SimultaneouslyAsync(Func<Task<CommandResult>> first, Func<Task<CommandResult>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[] { first, second }.Select(operation => Task.Run(async () =>
        {
            await start.Task;
            return await operation();
        })).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Two_administrators_disabling_each_other_at_the_same_time_leave_one_enabled()
    {
        SkipUnlessDisposable();

        for (var round = 0; round < Rounds; round++)
        {
            var (a, b) = await TwoAdministratorsOnlyAsync();

            var results = await SimultaneouslyAsync(
                () => InScopeAsync(accounts => accounts.SetEnabledAsync(b, enabled: false, a, Token)),
                () => InScopeAsync(accounts => accounts.SetEnabledAsync(a, enabled: false, b, Token)));

            Assert.Equal(1, results.Count(result => result.Succeeded));
            Assert.Equal(1, await EnabledAdministratorsAsync());
        }
    }

    [Fact]
    public async Task Two_administrators_demoting_each_other_at_the_same_time_leave_one_administrator()
    {
        SkipUnlessDisposable();

        for (var round = 0; round < Rounds; round++)
        {
            var (a, b) = await TwoAdministratorsOnlyAsync();
            var stampA = await InScopeAsync(async accounts => (await accounts.GetAsync(a, Token))!.ConcurrencyStamp);
            var stampB = await InScopeAsync(async accounts => (await accounts.GetAsync(b, Token))!.ConcurrencyStamp);

            var results = await SimultaneouslyAsync(
                () => InScopeAsync(accounts => accounts.UpdateAsync(b, new UserAccountInput("Administrador De Prueba", Roles.Operator, stampB), a, Token)),
                () => InScopeAsync(accounts => accounts.UpdateAsync(a, new UserAccountInput("Administrador De Prueba", Roles.Operator, stampA), b, Token)));

            Assert.Equal(1, results.Count(result => result.Succeeded));
            Assert.Equal(1, await EnabledAdministratorsAsync());

            // Ninguna cuenta queda sin rol ni con dos roles.
            await using var context = database.CreateContext();
            var roleCounts = await context.UserRoles.Where(link => link.UserId == a || link.UserId == b)
                .GroupBy(link => link.UserId).Select(group => group.Count()).ToListAsync(Token);
            Assert.Equal(new[] { 1, 1 }, roleCounts);
        }
    }

    [Fact]
    public async Task An_account_change_waits_while_another_one_holds_the_accounts_lock()
    {
        SkipUnlessDisposable();
        var (a, b) = await TwoAdministratorsOnlyAsync();
        var target = (await InScopeAsync(accounts => accounts.CreateAsync(Administrator("objetivo") with { Role = Roles.Operator }, a, Token))).Value!;

        await using var holder = database.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(Token);
        Assert.True(await holder.Database.TryAcquireExclusiveAsync(ApplicationLocks.Accounts, TimeSpan.FromSeconds(5), Token));

        var clock = Stopwatch.StartNew();
        var change = Task.Run(() => InScopeAsync(accounts => accounts.SetEnabledAsync(target, enabled: false, b, Token)), Token);
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        Assert.False(change.IsCompleted, "El cambio no esperó al bloqueo de cuentas.");
        await transaction.RollbackAsync(Token);

        var result = await change;
        Assert.True(result.Succeeded);
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Self_rules_hold_when_the_id_is_written_in_uppercase()
    {
        SkipUnlessDisposable();
        var (a, _) = await TwoAdministratorsOnlyAsync();

        // SQL Server encuentra la cuenta con el GUID en mayúsculas: la regla debe reconocer que es la propia.
        var disable = await InScopeAsync(accounts => accounts.SetEnabledAsync(a.ToUpperInvariant(), enabled: false, a, Token));
        var reset = await InScopeAsync(accounts => accounts.ResetPasswordAsync(a.ToUpperInvariant(), "Nueva123456789", "Nueva123456789", a, Token));

        Assert.Equal(CommandStatus.Conflict, disable.Status);
        Assert.Contains("propia cuenta", disable.Message);
        Assert.Equal(CommandStatus.Conflict, reset.Status);
        Assert.Contains("Cambiar contraseña", reset.Message);
    }

    [Fact]
    public async Task The_initial_administrator_command_creates_only_one_account_under_concurrency()
    {
        SkipUnlessDisposable();

        // Sin administradores habilitados, dos ejecuciones simultáneas del comando solo deben crear uno.
        await TwoAdministratorsOnlyAsync();
        var survivors = await InScopeAsync(async accounts => (await accounts.ListAsync(Token))
            .Where(account => account is { Role: Roles.Administrator, IsEnabled: true }).Select(account => account.Id).ToList());
        await using (var context = database.CreateContext())
        {
            // Deshabilitar directamente en la base desechable (no hay otro administrador que pueda hacerlo).
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE [AspNetUsers] SET [LockoutEnd] = '9999-12-31T23:59:59.9999999+00:00', [LockoutEnabled] = 1 WHERE [Id] IN ({0}, {1})",
                [survivors[0], survivors[1]],
                Token);
        }

        var first = Administrator("comando");
        var second = Administrator("comando");
        var results = await Task.WhenAll(
            Task.Run(() => InScopeAsync(accounts => accounts.CreateInitialAdministratorAsync(first.Email, first.FullName, first.Password, Token))),
            Task.Run(() => InScopeAsync(accounts => accounts.CreateInitialAdministratorAsync(second.Email, second.FullName, second.Password, Token))));

        Assert.Equal(1, results.Count(result => result.Outcome == InitialAdministratorOutcome.Created));
        Assert.Equal(1, results.Count(result => result.Outcome == InitialAdministratorOutcome.AlreadyExists));
        Assert.Equal(1, await EnabledAdministratorsAsync());
    }

    [Fact]
    public void The_disposable_database_is_never_the_shared_one()
    {
        SkipUnlessDisposable();

        Assert.StartsWith("FixTrackPrueba_", new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog, StringComparison.Ordinal);
    }
}
