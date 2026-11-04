using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nodus.Api.Data;
using Nodus.Api.DTOs;
using Nodus.Api.Entities;

namespace Nodus.Api.Services;

public class AuthService(AppDbContext dbContext, IOptions<JwtSettings> jwtOptions)
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public async Task<AuthResponse> RegisterAsync(string email, string password, string nickname)
    {
        email = NormalizeEmail(email);
        nickname = (nickname ?? string.Empty).Trim();

        ValidateEmail(email);
        ValidatePassword(password);
        if (nickname.Length is < 2 or > 50)
            throw new AuthException("Нікнейм має містити від 2 до 50 символів");

        if (await dbContext.Users.AnyAsync(u => u.Email == email))
            throw new AuthException("Користувач з таким email вже існує");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Nickname = nickname,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new AuthException("Користувач з таким email вже існує");
        }

        return BuildAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(string email, string password)
    {
        email = NormalizeEmail(email);

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user?.PasswordHash is null || string.IsNullOrEmpty(password)
            || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            throw new AuthException("Невірний email або пароль", StatusCodes.Status401Unauthorized);
        }

        return BuildAuthResponse(user);
    }

    public async Task<UserProfileDto> GetUserProfileAsync(Guid userId)
    {
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw new AuthException("Користувача не знайдено", StatusCodes.Status404NotFound);

        return ToDto(user);
    }

    public async Task<bool> DeleteUserAccountAsync(Guid userId)
    {
        var user = await dbContext.Users.FindAsync(userId);
        if (user is null) return false;

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync();
        return true;
    }

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

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static void ValidateEmail(string email)
    {
        if (email.Length is 0 or > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new AuthException("Некоректний формат email");
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new AuthException("Пароль має містити щонайменше 8 символів");
        if (password.Length > 72)
            throw new AuthException("Пароль не може бути довшим за 72 символи");
    }
}
