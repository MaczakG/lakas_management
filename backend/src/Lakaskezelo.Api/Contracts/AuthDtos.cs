namespace Lakaskezelo.Api.Contracts;

public record LoginRequest(string Email, string Password);
public record LoginChallengeResponse(Guid ChallengeId);
public record VerifyTwoFactorRequest(Guid ChallengeId, string Code);
public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
public record UserDto(Guid Id, string Email, string FullName, bool IsActive, DateTime CreatedAt);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record UpsertUserRequest(string Email, string FullName, bool IsActive, string? Password);
