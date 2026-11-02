using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record LoginRequest(string Email, string Password);
public record LoginChallengeResponse(Guid ChallengeId);
public record VerifyTwoFactorRequest(Guid ChallengeId, string Code);
public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User, string CompanyName);
public record MeResponse(UserDto User, string CompanyName);
// Pending: meghívott, aki még nem fogadta el a meghívót (vagy meg nem erősített regisztráció).
public record UserDto(Guid Id, string Email, string FullName, bool IsActive, DateTime CreatedAt, UserRole Role, bool Pending);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record InviteUserRequest(string Email, string FullName, UserRole Role);
public record UpdateUserRequest(string Email, string FullName, bool IsActive, UserRole Role);
public record RegisterRequest(string CompanyName, string FullName, string Email, string Password, bool AcceptTerms);
public record TokenRequest(string Token);
public record InviteInfoResponse(string Email, string FullName, string CompanyName);
public record AcceptInviteRequest(string Token, string? FullName, string Password);

public record PlatformAdminDto(Guid Id, string Email, string FullName);
public record PlatformAuthResponse(string AccessToken, DateTime ExpiresAt, PlatformAdminDto Admin);
