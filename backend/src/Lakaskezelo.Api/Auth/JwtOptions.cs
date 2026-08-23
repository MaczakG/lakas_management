namespace Lakaskezelo.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    // Must come from configuration/environment in every real environment — never hard-code a
    // production value here. At least 32 bytes so HMAC-SHA256 signing is valid.
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 120;
}
