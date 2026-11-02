namespace Lakaskezelo.Domain.Enums;

// Az e-mailben kiküldött egyszer használható linkek fajtája (ld. PasswordResetToken).
public enum UserTokenPurpose
{
    PasswordReset = 0,
    EmailVerification = 1,
    Invitation = 2,
}
