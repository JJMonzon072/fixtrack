using FixTrack.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace FixTrack.Web.Security;

/// <summary>
/// Políticas de autorización por operación. Toda página exige al menos <see cref="Workshop"/> (política de respaldo):
/// una cuenta sin rol no accede a nada.
/// <list type="table">
/// <item><term><see cref="Workshop"/></term><description>Administrador u Operador: panel, clientes, equipos, órdenes y consulta de técnicos.</description></item>
/// <item><term><see cref="ManageTechnicians"/></term><description>Administrador: registrar, editar, desactivar y reactivar técnicos.</description></item>
/// <item><term><see cref="ManageAccounts"/></term><description>Administrador: cuentas de acceso y roles.</description></item>
/// </list>
/// </summary>
public static class AuthorizationPolicies
{
    public const string Workshop = "Taller";
    public const string ManageTechnicians = "GestionarTecnicos";
    public const string ManageAccounts = "GestionarCuentas";

    public static void Configure(AuthorizationOptions options)
    {
        var workshop = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(Roles.Administrator, Roles.Operator)
            .Build();

        options.AddPolicy(Workshop, workshop);
        options.AddPolicy(ManageTechnicians, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Administrator));
        options.AddPolicy(ManageAccounts, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Administrator));

        // Lo que no declare otra política (o [AllowAnonymous]) exige una cuenta con rol del taller.
        options.FallbackPolicy = workshop;
    }
}
