using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FixTrack.Infrastructure.Identity;

/// <summary>Agrega a la sesión el nombre visible de la cuenta (claim <see cref="FullNameClaim"/>).</summary>
public sealed class FixTrackClaimsPrincipalFactory(
    UserManager<FixTrackUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<FixTrackUser, IdentityRole>(userManager, roleManager, options)
{
    public const string FullNameClaim = "fixtrack:nombre";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(FixTrackUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(FullNameClaim, user.FullName));
        return identity;
    }
}
