using System.Security.Cryptography;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Api.Auth;

public static class SecurityStamp
{
    // A JWT-be írt claim neve — a Program.cs OnTokenValidated ezt veti össze a User.SecurityStamp-pel.
    public const string ClaimType = "stamp";

    public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // Minden korábban kiadott token érvénytelenítése (jelszócsere, e-mail-változás, inaktiválás).
    public static void Rotate(User user) => user.SecurityStamp = New();
}
