using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Lakaskezelo.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Token has no sub/nameidentifier claim.");
        return Guid.Parse(raw);
    }
}
