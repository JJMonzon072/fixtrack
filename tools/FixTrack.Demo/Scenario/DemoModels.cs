using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;

namespace FixTrack.Demo.Scenario;

/// <summary>Quién hace una operación del escenario.</summary>
internal enum DemoActor
{
    /// <summary>Lo que en producción se hace desde la consola (crear el primer administrador o la cuenta de consulta).</summary>
    System,
    Administrator,
    Operator,
}

internal sealed record DemoAccount(string FullName, string Email, string Role);

internal sealed record DemoTechnician(string Key, string FullName, string Specialty, string? Phone, string? Email);

internal sealed record DemoCustomer(string Key, string FullName, string Phone, string? Email, string? Notes);

internal sealed record DemoDevice(string Key, string CustomerKey, DeviceType Type, string Brand, string Model, string SerialNumber, string? Notes);

/// <summary>Una orden tal como se recibe; <see cref="Steps"/> es lo que le pasa después, en orden.</summary>
internal sealed record DemoOrder(
    string Key,
    string DeviceKey,
    DateTime ReceivedAtUtc,
    DemoActor ReceivedBy,
    string ReportedIssue,
    ServiceOrderPriority Priority,
    string? TechnicianKey,
    DateTime? PromisedAtUtc,
    decimal? EstimatedCost,
    string? IntakeNotes,
    IReadOnlyList<DemoOrderStep> Steps);

internal abstract record DemoOrderStep(DateTime AtUtc, DemoActor By);

internal sealed record AssignStep(DateTime AtUtc, DemoActor By, string TechnicianKey, string? Note) : DemoOrderStep(AtUtc, By);

internal sealed record DiagnosisStep(DateTime AtUtc, DemoActor By, string Text) : DemoOrderStep(AtUtc, By);

internal sealed record PlanStep(DateTime AtUtc, DemoActor By, ServiceOrderPriority Priority, DateTime? PromisedAtUtc, decimal? EstimatedCost)
    : DemoOrderStep(AtUtc, By);

internal sealed record StatusStep(DateTime AtUtc, DemoActor By, ServiceOrderStatus Status, string? Note, decimal? FinalCost)
    : DemoOrderStep(AtUtc, By);

/// <summary>Una operación de la línea de tiempo: se ejecutan en orden cronológico, como ocurrirían en el taller.</summary>
internal abstract record DemoEvent(DateTime AtUtc, DemoActor By);

internal sealed record CreateTechnicianEvent(DateTime AtUtc, DemoActor By, DemoTechnician Technician) : DemoEvent(AtUtc, By);

internal sealed record CreateCustomerEvent(DateTime AtUtc, DemoActor By, DemoCustomer Customer) : DemoEvent(AtUtc, By);

internal sealed record RegisterDeviceEvent(DateTime AtUtc, DemoActor By, DemoDevice Device) : DemoEvent(AtUtc, By);

internal sealed record ReceiveOrderEvent(DateTime AtUtc, DemoActor By, DemoOrder Order) : DemoEvent(AtUtc, By);

internal sealed record OrderStepEvent(DemoOrder Order, DemoOrderStep Step) : DemoEvent(Step.AtUtc, Step.By);
