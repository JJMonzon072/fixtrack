using FixTrack.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace FixTrack.Web.Security;

/// <summary>
/// Acceso por rol a un grupo de operaciones. Los roles de <see cref="FullAccessRoles"/> pueden todo; el rol
/// <see cref="Roles.ReadOnly"/> (Consulta) solo puede abrir páginas: cualquier solicitud que no sea GET o HEAD (registrar,
/// modificar, desactivar, cambiar la contraseña…) se rechaza con 403. Como todas las políticas usan este requisito, una
/// acción nueva queda protegida para el rol de consulta sin declararlo.
/// </summary>
public sealed class RoleAccessRequirement(params string[] fullAccessRoles) : IAuthorizationRequirement
{
    public IReadOnlyList<string> FullAccessRoles { get; } = fullAccessRoles;
}

/// <summary>
/// Evalúa <see cref="RoleAccessRequirement"/>. En cada solicitud, el middleware de autorización pasa la solicitud como
/// recurso: para el rol de consulta se decide por su método. Las vistas preguntan sin recurso solo para decidir qué
/// enlaces y botones mostrar (también en una página de error de un POST): la cuenta de consulta puede abrir esas
/// pantallas, y cualquier envío se vuelve a evaluar con la solicitud. Un recurso de otro tipo se rechaza.
/// </summary>
public sealed class RoleAccessHandler : AuthorizationHandler<RoleAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleAccessRequirement requirement)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (requirement.FullAccessRoles.Any(user.IsInRole) || (user.IsInRole(Roles.ReadOnly) && ReadOnlyAllows(context.Resource)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>¿Puede la cuenta de consulta lo que se evalúa? Solo abrir páginas, o ver sus enlaces.</summary>
    public static bool ReadOnlyAllows(object? resource) => resource switch
    {
        HttpContext http => IsReadOnlyRequest(http),
        null => true,
        _ => false,
    };

    /// <summary>Solicitudes que no cambian nada: abrir una página o pedir solo sus encabezados.</summary>
    public static bool IsReadOnlyRequest(HttpContext http) =>
        HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
}
