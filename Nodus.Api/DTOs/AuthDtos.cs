namespace Nodus.Api.DTOs;

public record GoogleAuthRequest(string IdToken);
public record RegisterRequest(string Email, string Password, string Nickname);
public record LoginRequest(string Email, string Password);
public record UserProfileDto(Guid Id, string Email, string Nickname, string? AvatarUrl, DateTime CreatedAt);
public record AuthResponse(string AccessToken, UserProfileDto User);