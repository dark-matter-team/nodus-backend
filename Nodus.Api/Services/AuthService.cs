using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nodus.Api.Data;
using Nodus.Api.DTOs;
using Nodus.Api.Entities;
using Nodus.Api.Exceptions;
using Npgsql;

namespace Nodus.Api.Services;

public class AuthService(
    AppDbContext dbContext,
    IOptions<JwtSettings> jwtOptions,
    IOptions<GoogleSettings> googleOptions,
    IEmailService emailService)
{
    private readonly JwtSettings _jwt = jwtOptions.Value;
    private readonly GoogleSettings _google = googleOptions.Value;

    public async Task<AuthResponse> RegisterAsync(string email, string password, string nickname)
    {
        email = NormalizeEmail(email);
        nickname = (nickname ?? string.Empty).Trim();

        ValidateEmail(email);
        ValidatePassword(password);

        if (nickname.Length is < 2 or > 50 || nickname.Contains('@'))
            throw new ValidationException("Nickname must be between 2 and 50 characters and cannot contain '@'");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Nickname = nickname,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow,
            AvatarUrl = $"https://api.dicebear.com/7.x/bottts/svg?seed={Uri.EscapeDataString(nickname)}"
        };

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("A user with this email or nickname already exists");
        }

        return BuildAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(string? email, string? password, string? nickname)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(nickname))
            throw new ValidationException("Email or nickname is required");

        var user = !string.IsNullOrWhiteSpace(email)
            ? await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == NormalizeEmail(email))
            : await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Nickname.ToLower() == nickname!.Trim().ToLowerInvariant());

        if (user is null ||
            user.PasswordHash is null ||
            string.IsNullOrEmpty(password) ||
            !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid credentials");
        }

        return BuildAuthResponse(user);
    }
    public async Task<AuthResponse> GoogleLoginAsync(string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new ValidationException("Google ID token is required");

        var allowedAudiences = _google.GetAllClientIds();
        if (allowedAudiences.Count == 0)
            throw new InvalidOperationException("Google OAuth client IDs are not configured.");

        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = allowedAudiences
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        }
        catch (Exception ex) when (ex is InvalidJwtException
                                       or FormatException
                                       or ArgumentException
                                       or Newtonsoft.Json.JsonException)
        {
            throw new UnauthorizedException("Invalid Google token");
        }

        if (!payload.EmailVerified || string.IsNullOrEmpty(payload.Email))
            throw new UnauthorizedException("Google account email is missing or not verified");

        var email = NormalizeEmail(payload.Email);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.GoogleId == payload.Subject || u.Email == email);

        if (user is not null)
        {
            if (string.IsNullOrEmpty(user.GoogleId))
            {
                user.GoogleId = payload.Subject;
                await dbContext.SaveChangesAsync();
            }

            return BuildAuthResponse(user);
        }

        var nickname = await GenerateUniqueNicknameAsync(payload.Name, email);

        user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Nickname = nickname,
            PasswordHash = null,
            AvatarUrl = payload.Picture,
            CreatedAt = DateTime.UtcNow,
            GoogleId = payload.Subject
        };

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("A user with this email or nickname already exists");
        }

        return BuildAuthResponse(user);
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        ValidateEmail(email);

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
            return;

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        user.PasswordResetCode = code;
        user.PasswordResetCodeExpiresAt = DateTime.UtcNow.AddMinutes(15);

        await dbContext.SaveChangesAsync(cancellationToken);

        await emailService.SendEmailAsync(
            user.Email,
            "Nodus — Password Reset Code",
            $"Your password reset code is: {code}\nThis code is valid for 15 minutes.",
            cancellationToken: cancellationToken);
    }

    public async Task VerifyResetCodeAsync(string email, string code)
    {
        email = NormalizeEmail(email);
        var normalizedCode = (code ?? string.Empty).Trim();

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user is null ||
            string.IsNullOrEmpty(user.PasswordResetCode) ||
            user.PasswordResetCode != normalizedCode ||
            user.PasswordResetCodeExpiresAt is null ||
            user.PasswordResetCodeExpiresAt <= DateTime.UtcNow)
        {
            throw new ValidationException("Invalid or expired reset code");
        }
    }

    public async Task ResetPasswordAsync(string email, string code, string newPassword)
    {
        email = NormalizeEmail(email);
        var normalizedCode = (code ?? string.Empty).Trim();
        ValidatePassword(newPassword);

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null ||
            string.IsNullOrEmpty(user.PasswordResetCode) ||
            user.PasswordResetCode != normalizedCode ||
            user.PasswordResetCodeExpiresAt is null ||
            user.PasswordResetCodeExpiresAt <= DateTime.UtcNow)
        {
            throw new ValidationException("Invalid or expired reset code");
        }

        user.PasswordResetCode = null;
        user.PasswordResetCodeExpiresAt = null;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        await dbContext.SaveChangesAsync();
    }

    public async Task<UserProfileDto> GetUserProfileAsync(Guid userId)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new NotFoundException("User not found");

        return ToDto(user);
    }

    public async Task DeleteUserAccountAsync(Guid userId)
    {
        var user = await dbContext.Users.FindAsync(userId)
            ?? throw new NotFoundException("User not found");

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync();
    }

    private async Task<string> GenerateUniqueNicknameAsync(string? rawName, string email)
    {
        var baseName = SanitizeNickname(rawName);
        if (baseName.Length < 2)
            baseName = SanitizeNickname(email.Split('@')[0]);
        if (baseName.Length < 2)
            baseName = "user";

        if (baseName.Length > 50)
            baseName = baseName[..50].Trim();
        if (baseName.Length < 2)
            baseName = "user";

        var candidate = baseName;
        var normalizedCandidate = candidate.ToLowerInvariant();

        if (!await dbContext.Users.AnyAsync(u => u.Nickname.ToLower() == normalizedCandidate))
            return candidate;

        var prefix = baseName.Length > 43 ? baseName[..43].TrimEnd() : baseName;
        if (prefix.Length == 0)
            prefix = "user";

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = RandomNumberGenerator.GetInt32(100000, 1000000);
            candidate = $"{prefix}_{suffix}";
            normalizedCandidate = candidate.ToLowerInvariant();

            if (!await dbContext.Users.AnyAsync(u => u.Nickname.ToLower() == normalizedCandidate))
                return candidate;
        }

        return $"user_{Guid.NewGuid():N}"[..20];
    }

    private static string SanitizeNickname(string? value) =>
        (value ?? string.Empty).Replace("@", string.Empty).Trim();

    private AuthResponse BuildAuthResponse(User user) => new(
        GenerateJwtToken(user),
        ToDto(user)
    );

    private static UserProfileDto ToDto(User user) => new(
        user.Id,
        user.Email,
        user.Nickname,
        user.AvatarUrl,
        user.CreatedAt
    );

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Expires = DateTime.UtcNow.AddMinutes(_jwt.ExpiresMinutes),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    private static void ValidateEmail(string email)
    {
        if (email.Length is 0 or > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new ValidationException("Invalid email format");
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new ValidationException("Password must be at least 8 characters long");
        if (password.Length > 72)
            throw new ValidationException("Password cannot exceed 72 characters");
    }
}