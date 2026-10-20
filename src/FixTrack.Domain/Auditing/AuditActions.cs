namespace FixTrack.Domain.Auditing;

/// <summary>Tipos de registro y de cambio que se auditan.</summary>
public static class AuditActions
{
    public const string Order = "orden";
    public const string Technician = "tecnico";
    public const string Customer = "cliente";
    public const string Account = "cuenta";

    public const string OrderRegistered = "orden.registrada";
    public const string TechnicianAssigned = "orden.tecnico-asignado";
    public const string TechnicianReassigned = "orden.tecnico-reasignado";
    public const string StatusChanged = "orden.estado";
    public const string OrderCancelled = "orden.cancelada";
    public const string OrderDelivered = "orden.entregada";
    public const string PlanChanged = "orden.planificacion";
    public const string DiagnosisRecorded = "orden.diagnostico";

    public const string TechnicianDeactivated = "tecnico.desactivado";
    public const string TechnicianReactivated = "tecnico.reactivado";

    public const string CustomerDeactivated = "cliente.desactivado";
    public const string CustomerReactivated = "cliente.reactivado";

    public const string AccountCreated = "cuenta.creada";
    public const string AccountEdited = "cuenta.editada";
    public const string AccountDisabled = "cuenta.deshabilitada";
    public const string AccountEnabled = "cuenta.habilitada";
    public const string AccountUnlocked = "cuenta.desbloqueada";
    public const string PasswordReset = "cuenta.contrasena-restablecida";
    public const string InitialAdministrator = "cuenta.administrador-inicial";
}
