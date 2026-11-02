using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lakaskezelo.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lakaskezelo.Api.Auth;

// A szerepkör nem a tokenben van (kérésenként az adatbázisból jön, ld. Program.cs OnTokenValidated). A
// platform-admin tokenje a "kind"="platform" claimmel különül el: csak a /api/platform végpontokat éri el.
public class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public const string KindClaim = "kind";
    public const string PlatformKind = "platform";

    public (string Token, DateTime ExpiresAt) CreatePlatformToken(PlatformAdmin admin) => Create(
    [
        new(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
        new(JwtRegisteredClaimNames.Email, admin.Email),
        new(ClaimTypes.Name, admin.FullName),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new(SecurityStamp.ClaimType, admin.SecurityStamp),
        new(KindClaim, PlatformKind),
    ]);

    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user) => Create(
    [
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(ClaimTypes.Name, user.FullName),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new(SecurityStamp.ClaimType, user.SecurityStamp),
    ]);

    private (string Token, DateTime ExpiresAt) Create(List<Claim> claims)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
