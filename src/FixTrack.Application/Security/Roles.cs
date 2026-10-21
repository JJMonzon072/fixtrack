namespace FixTrack.Application.Security;

/// <summary>
/// Roles de las cuentas de acceso. Los permisos de cada uno se definen como políticas de autorización en la
/// capa de presentación (por ejemplo, solo el administrador gestiona técnicos y cuentas).
/// </summary>
public static class Roles
{
    /// <summary>Administración completa: clientes, equipos, técnicos, órdenes y cuentas de acceso.</summary>
    public const string Administrator = "Administrador";

    /// <summary>Gestión operativa: clientes, equipos y órdenes. Consulta técnicos, pero no los administra ni gestiona cuentas.</summary>
    public const string Operator = "Operador";

    /// <summary>
    /// Solo lectura: abre todas las pantallas (incluidas usuarios y auditoría), pero no registra, modifica ni elimina
    /// nada. Para demostraciones y para preparar el manual de usuario.
    /// </summary>
    public const string ReadOnly = "Consulta";

    public static IReadOnlyList<string> All { get; } = [Administrator, Operator, ReadOnly];

    public static bool IsValid(string? role) => role is not null && All.Contains(role);
}
