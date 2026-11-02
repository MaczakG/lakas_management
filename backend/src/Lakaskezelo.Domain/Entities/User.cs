using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

public class User : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public UserRole Role { get; set; } = UserRole.Member;

    // Mikor igazolta a felhasználó, hogy az e-mail cím az övé (regisztráció megerősítése vagy meghívás
    // elfogadása). Amíg null, nem tud belépni. Meghívottnál a jelszó is ekkor áll be (addig üres).
    public DateTime? EmailVerifiedAt { get; set; }

    // A kiadott JWT-k "stamp" claimje ezzel egyezik — jelszócserénél, e-mail-változásnál és
    // inaktiválásnál új értéket kap, így a korábban kiadott tokenek azonnal érvénytelenné válnak
    // (ld. Api/Auth/SecurityStamp és a JwtBearer OnTokenValidated ellenőrzése a Program.cs-ben).
    public string SecurityStamp { get; set; } = string.Empty;

    // Fiókzárolás: egymás utáni sikertelen jelszó-/kódpróbálkozások száma, és meddig zárolt a fiók
    // (ld. AuthController). Sikeres, teljes bejelentkezés nullázza.
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }
}
