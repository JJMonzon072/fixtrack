using FixTrack.Application.Reports;
using FixTrack.Application.Security;
using FixTrack.Demo;
using FixTrack.Demo.Scenario;
using FixTrack.Domain.Auditing;
using FixTrack.Domain.Customers;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FixTrack.Tests.Demo;

/// <summary>
/// La carga completa de la demostración con los servicios reales de FixTrack sobre SQLite: lo que queda en la base debe
/// ser coherente con las reglas del dominio y con lo que muestran los reportes.
/// </summary>
public sealed class DemoLoaderTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly ServiceProvider _services;

    public DemoLoaderTests()
    {
        _services = DemoServices.Create("Server=pruebas-sin-base-real;Database=NoUsar;", services =>
        {
            services.RemoveAll<DbContextOptions<FixTrackDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FixTrackDbContext>>();
            services.RemoveAll<FixTrackDbContext>();
            services.AddDbContext<FixTrackDbContext>(options => options.UseSqlite(_database.Connection));
        });
    }

    internal static DemoPasswords Passwords { get; } = new(NewPassword(), NewPassword(), NewPassword());

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DemoScenario Scenario => DemoScenario.Create(DemoScenarioTests.Anchor, DemoServices.WorkshopTimeZone);

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task Loads_the_whole_workshop_through_the_application_services()
    {
        Assert.Equal(DemoLoadOutcome.Loaded, await LoadAsync());

        await using var db = _database.CreateContext();
        Assert.Equal(4, await db.Technicians.CountAsync(Token));
        Assert.Equal(10, await db.Customers.CountAsync(Token));
        Assert.Equal(15, await db.Devices.CountAsync(Token));
        var orders = await db.ServiceOrders.Include(order => order.StatusHistory).ToListAsync(Token);
        Assert.Equal(
            new Dictionary<ServiceOrderStatus, int>
            {
                [ServiceOrderStatus.Received] = 2,
                [ServiceOrderStatus.Diagnosing] = 2,
                [ServiceOrderStatus.InRepair] = 2,
                [ServiceOrderStatus.AwaitingParts] = 2,
                [ServiceOrderStatus.ReadyForPickup] = 2,
                [ServiceOrderStatus.Delivered] = 13,
                [ServiceOrderStatus.Cancelled] = 2,
            },
            orders.GroupBy(order => order.Status).ToDictionary(group => group.Key, group => group.Count()));

        // Los números de orden siguen el orden de recepción, como en un taller real.
        Assert.Equal(orders.OrderBy(order => order.ReceivedAtUtc).Select(order => order.Id), orders.OrderBy(order => order.Id).Select(order => order.Id));
    }

    [Fact]
    public async Task Every_history_follows_the_workflow_with_its_author()
    {
        await LoadAsync();
        var scenario = Scenario;

        await using var db = _database.CreateContext();
        foreach (var order in await db.ServiceOrders.Include(order => order.StatusHistory).ToListAsync(Token))
        {
            var history = order.StatusHistory.OrderBy(change => change.ChangedAtUtc).ThenBy(change => change.Id).ToList();
            Assert.Equal(ServiceOrderStatus.Received, history[0].ToStatus);
            Assert.All(history.Skip(1), change => Assert.True(
                ServiceOrderWorkflow.CanTransition(change.FromStatus!.Value, change.ToStatus),
                $"{order.Number}: {change.FromStatus} → {change.ToStatus}"));
            Assert.Equal(order.Status, history[^1].ToStatus);
            Assert.All(history, change => Assert.Contains(change.ChangedByName, new[] { scenario.Operator.FullName, scenario.Administrator.FullName }));
            Assert.Equal(order.Status == ServiceOrderStatus.Delivered, order.FinalCost is not null && order.DeliveredAtUtc is not null);
        }

        var audit = await db.AuditEvents.ToListAsync(Token);
        Assert.All(audit.Where(item => item.EntityType == AuditActions.Order), item => Assert.NotEqual("Sistema", item.UserName));
        Assert.Equal(25, audit.Count(item => item.Action == AuditActions.OrderRegistered));
        Assert.Equal(3, audit.Count(item => item.Action == AuditActions.TechnicianReassigned));
        Assert.Equal(3, audit.Count(item => item.Action == AuditActions.PlanChanged));
        Assert.True(audit.Max(item => item.OccurredAtUtc) <= DemoScenarioTests.Anchor);
    }

    [Fact]
    public async Task The_data_shows_late_deliveries_cost_differences_and_open_problems()
    {
        await LoadAsync();

        await using var db = _database.CreateContext();
        var orders = await db.ServiceOrders.ToListAsync(Token);
        var delivered = orders.Where(order => order.Status == ServiceOrderStatus.Delivered).ToList();
        Assert.Equal(4, delivered.Count(order => order.DeliveredAtUtc > order.PromisedAtUtc));
        Assert.Equal(8, delivered.Count(order => order.DeliveredAtUtc <= order.PromisedAtUtc));
        Assert.Single(delivered, order => order.PromisedAtUtc is null && order.EstimatedCost is null);
        Assert.Equal(6, delivered.Count(order => order.EstimatedCost is not null && order.FinalCost != order.EstimatedCost));

        var open = orders.Where(order => ServiceOrderWorkflow.IsOpen(order.Status)).ToList();
        Assert.Equal(2, open.Count(order => ServiceOrderSchedule.IsOverdue(order.Status, order.PromisedAtUtc, DemoScenarioTests.Anchor)));
        Assert.Equal(2, open.Count(order => order.TechnicianId is null));
        Assert.Equal(3, await db.Set<ServiceOrderAssignment>().CountAsync(assignment => assignment.FromTechnicianId != null, Token));
    }

    [Fact]
    public async Task The_reports_add_up_to_the_scenario()
    {
        await LoadAsync();
        _services.GetRequiredService<DemoClock>().Set(DemoScenarioTests.Anchor);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DemoScenarioTests.Anchor, DemoServices.WorkshopTimeZone));

        await using var scope = _services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<IReportService>().GetAsync(
            new ReportFilter(ReportPeriod.ForQuickRange(ReportQuickRange.Last90Days, today, DemoServices.WorkshopTimeZone)), Token);

        Assert.Equal(25, report.Received);
        Assert.Equal(13, report.Delivered);
        Assert.Equal(2, report.Cancelled);
        Assert.Equal(12, report.DeliveredWithPromise);
        Assert.Equal(4, report.DeliveredLate);
        Assert.Equal(67, report.OnTimePercent);
        Assert.Equal(6550m, report.Revenue);
        Assert.Equal(12, report.DeliveredWithEstimate);
        Assert.Equal(175m, report.CostVariance);
        Assert.Equal(4, report.Technicians.Count(technician => technician.TechnicianId is not null));
        Assert.Equal(2, report.Technicians.Single(technician => technician.TechnicianId is null).OpenNow);
    }

    [Fact]
    public async Task The_three_accounts_sign_in_with_their_roles()
    {
        await LoadAsync();
        var scenario = Scenario;

        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<FixTrackUser>>();
        foreach (var (account, password) in new[]
        {
            (scenario.Administrator, Passwords.Administrator),
            (scenario.Operator, Passwords.Operator),
            (scenario.ReadOnly, Passwords.ReadOnly),
        })
        {
            var user = await users.FindByEmailAsync(account.Email);
            Assert.NotNull(user);
            Assert.True(await users.CheckPasswordAsync(user, password));
            Assert.Equal(new[] { account.Role }, await users.GetRolesAsync(user));
        }

        Assert.Equal(
            new[] { Roles.Administrator, Roles.Operator, Roles.ReadOnly },
            new[] { scenario.Administrator.Role, scenario.Operator.Role, scenario.ReadOnly.Role });
    }

    [Fact]
    public async Task Loading_again_changes_nothing()
    {
        await LoadAsync();
        await using (var db = _database.CreateContext())
        {
            // Quien visita la demostración puede registrar órdenes: volver a iniciar el entorno no debe fallar por eso.
            var device = await db.Devices.Include(item => item.Customer).FirstAsync(Token);
            db.ServiceOrders.Add(new ServiceOrder(device, "Orden de un visitante", ServiceOrderPriority.Low, DemoScenarioTests.Anchor, null, null, null));
            await db.SaveChangesAsync(Token);
        }

        Assert.Equal(DemoLoadOutcome.AlreadyLoaded, await LoadAsync());

        await using var check = _database.CreateContext();
        Assert.Equal(26, await check.ServiceOrders.CountAsync(Token));
        Assert.Equal(3, await check.Users.CountAsync(Token));
    }

    [Fact]
    public async Task A_database_with_other_data_is_left_untouched()
    {
        await using (var db = _database.CreateContext())
        {
            db.Customers.Add(new Customer("Cliente existente", "2222-0000", null, null, DemoScenarioTests.Anchor.AddDays(-1)));
            await db.SaveChangesAsync(Token);
        }

        var error = await Assert.ThrowsAsync<DemoException>(LoadAsync);

        Assert.Contains("no está vacía", error.Message, StringComparison.Ordinal);
        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.Customers.CountAsync(Token));
        Assert.False(await check.Users.AnyAsync(Token));
        Assert.False(await check.AuditEvents.AnyAsync(Token));
    }

    private Task<DemoLoadOutcome> LoadAsync() => new DemoLoader(_services).LoadAsync(Scenario, Passwords, Token);

    private static string NewPassword() => $"Demo{Guid.NewGuid():N}"[..20] + "Aa1";
}
