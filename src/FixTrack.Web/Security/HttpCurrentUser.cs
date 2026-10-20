using System.Security.Claims;
using FixTrack.Application.Common;
using FixTrack.Domain.Common;
using FixTrack.Infrastructure.Identity;

namespace FixTrack.Web.Security;

/// <summary>
/// Quién hace la solicitud: el Id de la cuenta y el nombre visible de la sesión. Sin una solicitud con sesión
/// (por ejemplo, un comando de consola), es <see cref="Actor.System"/>.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Actor Actor =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? new Actor(
                user.FindFirstValue(ClaimTypes.NameIdentifier),
                user.FindFirstValue(FixTrackClaimsPrincipalFactory.FullNameClaim) ?? user.Identity?.Name ?? "Cuenta sin nombre")
            : Actor.System;
}
