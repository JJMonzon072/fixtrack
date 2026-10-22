using System.Text.Json;
using FixTrack.Demo;
using FixTrack.Demo.Scenario;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Tests.Demo;

/// <summary>El escenario de la demostración, sin base de datos: tamaño, marcas de datos ficticios y fechas.</summary>
public sealed class DemoScenarioTests
{
    /// <summary>21 de octubre de 2026, 14:30 en Guatemala.</summary>
    internal static readonly DateTime Anchor = new(2026, 10, 21, 20, 30, 0, DateTimeKind.Utc);

    private static DemoScenario Scenario(DateTime? anchor = null) => DemoScenario.Create(anchor ?? Anchor, DemoServices.WorkshopTimeZone);

    [Fact]
    public void The_workshop_is_small_and_covers_every_status()
    {
        var scenario = Scenario();

        Assert.Equal(4, scenario.Technicians.Count);
        Assert.Equal(10, scenario.Customers.Count);
        Assert.Equal(15, scenario.Devices.Count);
        Assert.Equal(25, scenario.Orders.Count);
        Assert.All(scenario.Devices, device => Assert.Contains(scenario.Orders, order => order.DeviceKey == device.Key));
        Assert.All(scenario.Customers, customer => Assert.Contains(scenario.Devices, device => device.CustomerKey == customer.Key));
        Assert.Equal(Enum.GetValues<ServiceOrderStatus>().Order(), scenario.Orders.Select(FinalStatus).Distinct().Order());
    }

    [Fact]
    public void Every_record_is_visibly_fictitious()
    {
        var scenario = Scenario();
        string[] people = [
            .. scenario.Customers.Select(customer => customer.FullName),
            .. scenario.Technicians.Select(technician => technician.FullName),
            scenario.Administrator.FullName, scenario.Operator.FullName, scenario.ReadOnly.FullName,
        ];
        string[] emails = [
            .. scenario.Customers.Select(customer => customer.Email).OfType<string>(),
            .. scenario.Technicians.Select(technician => technician.Email).OfType<string>(),
            scenario.Administrator.Email, scenario.Operator.Email, scenario.ReadOnly.Email,
        ];

        Assert.All(people, name => Assert.EndsWith("(demo)", name, StringComparison.Ordinal));
        Assert.All(emails, email => Assert.EndsWith(".invalid", email, StringComparison.Ordinal));
        Assert.All(scenario.Customers.Select(customer => customer.Phone).Concat(scenario.Technicians.Select(technician => technician.Phone!)),
            phone => Assert.StartsWith("0000-", phone, StringComparison.Ordinal));
        Assert.All(scenario.Devices, device => Assert.StartsWith("DEMO-", device.SerialNumber, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2026-10-21T20:30:00Z")] // 14:30 en el taller
    [InlineData("2026-10-21T12:05:00Z")] // 06:05: antes de abrir
    [InlineData("2026-10-22T05:50:00Z")] // 23:50
    [InlineData("2027-03-01T06:00:00Z")] // medianoche
    public void Nothing_happens_after_the_load_and_each_order_moves_forward(string anchor)
    {
        var scenario = Scenario(DateTime.Parse(anchor, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        var timeline = scenario.Timeline();

        Assert.All(timeline, item => Assert.True(item.AtUtc <= scenario.AnchorUtc, $"{item} es posterior al ancla."));
        Assert.True(scenario.AccountsCreatedAtUtc < timeline[0].AtUtc);
        Assert.Equal(timeline.OrderBy(item => item.AtUtc), timeline);
        foreach (var order in scenario.Orders)
        {
            var times = order.Steps.Select(step => step.AtUtc).Prepend(order.ReceivedAtUtc).ToList();
            Assert.Equal(times.Order(), times);
            Assert.Equal(times.Count, times.Distinct().Count());
        }

        // Cada cliente y cada equipo existen antes de su primera orden.
        Assert.All(timeline.OfType<ReceiveOrderEvent>(), received =>
        {
            var device = scenario.DeviceOf(received.Order);
            Assert.Contains(timeline.TakeWhile(item => item != received), item => item is RegisterDeviceEvent registered && registered.Device == device);
            Assert.Contains(timeline.TakeWhile(item => item != received), item => item is CreateCustomerEvent created && created.Customer.Key == device.CustomerKey);
        });
    }

    [Fact]
    public void The_same_anchor_always_produces_the_same_data()
    {
        static string Snapshot(DemoScenario scenario) => JsonSerializer.Serialize(scenario.Timeline().Select(item => new
        {
            item.AtUtc,
            item.By,
            Kind = item.GetType().Name,
            Detail = item switch
            {
                OrderStepEvent step => JsonSerializer.Serialize<object>(step.Step),
                ReceiveOrderEvent received => JsonSerializer.Serialize(received.Order with { Steps = [] }),
                _ => item.ToString(),
            },
        }));

        Assert.Equal(Snapshot(Scenario()), Snapshot(Scenario()));
        Assert.NotEqual(Snapshot(Scenario()), Snapshot(Scenario(Anchor.AddDays(1))));
    }

    [Fact]
    public void Dates_follow_the_load_moment_so_the_demo_always_looks_current()
    {
        var scenario = Scenario();
        var first = scenario.Orders.Min(order => order.ReceivedAtUtc);

        Assert.InRange((scenario.AnchorUtc - first).TotalDays, 70, 75);
        Assert.Equal(2, scenario.Orders.Count(order => order.ReceivedAtUtc > scenario.AnchorUtc.AddHours(-4)));
        Assert.Equal(new DateTime(2026, 10, 21, 20, 30, 0, DateTimeKind.Utc), Scenario(Anchor.AddSeconds(42)).AnchorUtc);
    }

    internal static ServiceOrderStatus FinalStatus(DemoOrder order) =>
        order.Steps.OfType<StatusStep>().LastOrDefault()?.Status ?? ServiceOrderStatus.Received;
}
