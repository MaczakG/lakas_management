namespace Lakaskezelo.Domain.Enums;

// Cégen belüli szerepkör. Az Adminisztrátor kezeli a felhasználókat, a Beállításokat (és később az
// előfizetést); a Munkatárs minden napi munkát végezhet (ingatlanok, bérlők, rezsi, számlák).
public enum UserRole
{
    Admin = 0,
    Member = 1,
}
