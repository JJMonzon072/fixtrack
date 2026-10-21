using FixTrack.Application.Reports;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Tests.Infrastructure;

/// <summary>
/// Resultados esperados del escenario de <see cref="ReportSeed"/> para octubre de 2026. Los comparten las pruebas con
/// SQLite y con SQL Server: ambos motores deben calcular exactamente lo mismo.
/// </summary>
internal static class ReportExpectations
{
    public static void AssertOctoberTotals(OperationsReport report)
    {
        Assert.Equal((7, 4, 2), (report.Received, report.Delivered, report.Cancelled));
        Assert.Equal((3, 1, 67), (report.DeliveredWithPromise, report.DeliveredLate, report.OnTimePercent));
        Assert.Equal((1420.00m, 355.00m), (report.Revenue, report.AverageFinalCost));
        Assert.Equal((3, 1200.00m, 1220.00m), (report.DeliveredWithEstimate, report.EstimatedCostSum, report.FinalCostSumWithEstimate));
        Assert.Equal((20.00m, 1.7m), (report.CostVariance, report.CostVariancePercent));

        // A 84 h, B 72 h, E 120 h y K 168 h hasta la entrega; hasta el último paso a "Lista para entrega": 78, 48, 96 y 144 h.
        Assert.Equal(111d, report.AverageHoursToDelivery!.Value, precision: 6);
        Assert.Equal(91.5d, report.AverageHoursToReady!.Value, precision: 6);
        Assert.Equal(4, report.ReadyMeasured);
    }

    public static void AssertOctoberDistributions(OperationsReport report)
    {
        Assert.Equal(
            [
                (ServiceOrderStatus.Received, 1), (ServiceOrderStatus.Diagnosing, 0), (ServiceOrderStatus.InRepair, 1),
                (ServiceOrderStatus.AwaitingParts, 0), (ServiceOrderStatus.ReadyForPickup, 0), (ServiceOrderStatus.Delivered, 3),
                (ServiceOrderStatus.Cancelled, 2),
            ],
            report.ReceivedByStatus.Select(share => (share.Key, share.Count)));
        Assert.Equal(43, report.ReceivedByStatus.Single(share => share.Key == ServiceOrderStatus.Delivered).Percent);

        Assert.Equal(
            [
                (ServiceOrderPriority.Urgent, 2, 0, 0, (int?)null, (double?)null),
                (ServiceOrderPriority.High, 2, 1, 0, 100, 84d),
                (ServiceOrderPriority.Normal, 2, 3, 1, 50, 120d),
                (ServiceOrderPriority.Low, 1, 0, 0, null, null),
            ],
            report.Priorities.Select(row => (row.Priority, row.Received, row.Delivered, row.Late, row.OnTimePercent, row.AverageHoursToDelivery)));
    }

    public static void AssertOctoberSeries(OperationsReport report)
    {
        Assert.Equal(31, report.Series.Count);
        Assert.All(report.Series, point => Assert.Equal(point.Start, point.End));
        Assert.Equal(7, report.Series.Sum(point => point.Received));
        Assert.Equal((4, 1420.00m), (report.Series.Sum(point => point.Delivered), report.Series.Sum(point => point.Revenue)));

        int Received(int day) => report.Series[day - 1].Received;
        (int, decimal) Delivered(int day) => (report.Series[day - 1].Delivered, report.Series[day - 1].Revenue);
        Assert.Equal((1, 1, 1, 1, 1, 1, 1), (Received(1), Received(5), Received(10), Received(12), Received(20), Received(25), Received(31)));
        Assert.Equal(((1, 200.00m), (1, 450.00m), (1, 520.00m), (1, 250.00m)), (Delivered(3), Delivered(4), Delivered(12), Delivered(15)));
    }

    public static void AssertOctoberTechnicians(OperationsReport report, string suffix = "")
    {
        Assert.Equal(
            [
                ($"Carla Ruiz{suffix}", 4, 2, 1, 700.00m, (double?)102d, 1),
                ($"Luis Pérez{suffix}", 2, 2, 0, 720.00m, 120d, 0),
                ("Sin técnico asignado", 1, 0, 0, 0m, null, 2),
            ],
            report.Technicians.Select(row => (row.Name, row.Received, row.Delivered, row.Late, row.FinalCostSum, row.AverageHoursToDelivery, row.OpenNow)));
        Assert.Equal((50, 100, (int?)null), (report.Technicians[0].OnTimePercent, report.Technicians[1].OnTimePercent, report.Technicians[2].OnTimePercent));
    }
}
