using FixTrack.Application.Auditing;
using FixTrack.Domain.Auditing;

namespace FixTrack.Web.Models.Audit;

public sealed record AuditIndexViewModel(AuditArea Area, AuditSearchResult Result);

/// <summary>Textos e íconos del registro de auditoría.</summary>
public static class AuditDisplay
{
    public static IReadOnlyList<(AuditArea Area, string Label)> Areas { get; } =
    [
        (AuditArea.All, "Todas"),
        (AuditArea.Orders, "Órdenes"),
        (AuditArea.Technicians, "Técnicos"),
        (AuditArea.Customers, "Clientes"),
        (AuditArea.Accounts, "Cuentas"),
    ];

    public static string Slug(AuditArea area) => area switch
    {
        AuditArea.Orders => "ordenes",
        AuditArea.Technicians => "tecnicos",
        AuditArea.Customers => "clientes",
        AuditArea.Accounts => "cuentas",
        _ => "todas",
    };

    public static AuditArea ParseArea(string? slug) =>
        Areas.Select(item => item.Area).FirstOrDefault(area => Slug(area) == slug?.Trim().ToLowerInvariant());

    public static string ActionName(string action) => action switch
    {
        AuditActions.OrderRegistered => "Orden registrada",
        AuditActions.TechnicianAssigned => "Técnico asignado",
        AuditActions.TechnicianReassigned => "Técnico reasignado",
        AuditActions.StatusChanged => "Cambio de estado",
        AuditActions.OrderCancelled => "Orden cancelada",
        AuditActions.OrderDelivered => "Orden entregada",
        AuditActions.PlanChanged => "Planificación",
        AuditActions.DiagnosisRecorded => "Diagnóstico",
        AuditActions.TechnicianDeactivated => "Técnico desactivado",
        AuditActions.TechnicianReactivated => "Técnico reactivado",
        AuditActions.CustomerDeactivated => "Cliente desactivado",
        AuditActions.CustomerReactivated => "Cliente reactivado",
        AuditActions.AccountCreated => "Cuenta creada",
        AuditActions.AccountEdited => "Cuenta editada",
        AuditActions.AccountDisabled => "Cuenta deshabilitada",
        AuditActions.AccountEnabled => "Cuenta habilitada",
        AuditActions.AccountUnlocked => "Cuenta desbloqueada",
        AuditActions.PasswordReset => "Contraseña restablecida",
        AuditActions.InitialAdministrator => "Primer administrador",
        _ => action,
    };

    public static string Icon(string entityType) => entityType switch
    {
        AuditActions.Order => "clipboard-list",
        AuditActions.Technician => "wrench",
        AuditActions.Customer => "users",
        _ => "user-cog",
    };

    /// <summary>Controlador y acción de la página del registro afectado, y texto del enlace.</summary>
    public static (string Controller, string Action, string Text) Target(AuditEntry entry) => entry.EntityType switch
    {
        AuditActions.Order => ("ServiceOrders", "Details", "Ver orden"),
        AuditActions.Technician => ("Technicians", "Details", "Ver técnico"),
        AuditActions.Customer => ("Customers", "Details", "Ver cliente"),
        _ => ("Users", "Edit", "Ver cuenta"),
    };
}
