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
using Google.Apis.Auth;
using System.Net;

namespace Nodus.Api.Services;

public class AuthService(AppDbContext dbContext, IOptions<JwtSettings> jwtOptions, IOptions<GoogleSettings> googleOptions, IOptions<SmtpSettings> smtpOptions)
{
    private readonly JwtSettings _jwt = jwtOptions.Value;
    private readonly GoogleSettings _google = googleOptions.Value;
    private readonly SmtpSettings _smtp = smtpOptions.Value;
    public async Task<AuthResponse> RegisterAsync(string email, string password, string nickname)
    {
        email = NormalizeEmail(email);
        nickname = (nickname ?? string.Empty).Trim();

        ValidateEmail(email);
        ValidatePassword(password);
        if (nickname.Length is < 2 or > 50 || nickname.Contains('@'))
           throw new AuthException("Нікнейм має містити від 2 до 50 символів і не містити '@'");

        var normalizedNickname = nickname.ToLowerInvariant();
        if (await dbContext.Users.AnyAsync(u => u.Email == email || u.Nickname.ToLower() == normalizedNickname))
            throw new AuthException("Користувач з таким email або нікнеймом вже існує");

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
        catch (DbUpdateException)
        {
            throw new AuthException("Користувач з таким email вже існує");
        }

        return BuildAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(string? email, string? password, string? nickname)
    {
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(nickname))
        {
            throw new AuthException("Введіть email або нікнейм", StatusCodes.Status400BadRequest);
        }

        User? user = null;

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = NormalizeEmail(email);
            user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        }
        else if (!string.IsNullOrWhiteSpace(nickname))
        {
            var normalizedNickname = nickname.Trim().ToLowerInvariant();
            user = await dbContext.Users.FirstOrDefaultAsync(u => u.Nickname.ToLower() == normalizedNickname);
        }
        if (user is not null && user.PasswordHash is null)  
        {
            throw new AuthException("Цей акаунт створено через Google. Увійдіть за допомогою Google", StatusCodes.Status401Unauthorized);
        }
        else if (user is null || string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            throw new AuthException("Невірний email/нікнейм або пароль", StatusCodes.Status401Unauthorized);
        }

        return BuildAuthResponse(user);
    }
    public async Task<AuthResponse> GoogleLoginAsync(string IdToken)
    {
        if (string.IsNullOrWhiteSpace(IdToken))
        {
            throw new AuthException("Токен авторизації Google є обов'язковим", StatusCodes.Status400BadRequest);
        }
        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = [_google.ClientId, "407408718192.apps.googleusercontent.com"]
        };
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(IdToken, settings);
        }
        catch (InvalidJwtException)
        {
            throw new AuthException("Недійсний Google токен", StatusCodes.Status401Unauthorized);
        }
        if (!payload.EmailVerified || string.IsNullOrEmpty(payload.Email))
        {
            throw new AuthException("Email облікового запису Google не підтверджено або відсутній", StatusCodes.Status401Unauthorized);
        }
        var email = NormalizeEmail(payload.Email);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);

        if(user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                Nickname = payload.Name ?? email.Split('@')[0],
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
            catch (DbUpdateException)
            {
                throw new AuthException("Не вдалося створити користувача. Можливо, такий нікнейм або email вже зайнято");
            }
        }
        return BuildAuthResponse(user);
    }

    public async Task ForgotPasswordAsync(string email)
    {
        email = NormalizeEmail(email);
        ValidateEmail(email);

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email)
                   ?? throw new AuthException("Користувача з таким email не знайдено", StatusCodes.Status404NotFound);

        var code = Random.Shared.Next(100000, 1000000).ToString();
        user.PasswordResetCode = code;
        user.PasswordResetCodeExpiresAt = DateTime.UtcNow.AddMinutes(15);

        await dbContext.SaveChangesAsync();
        await SendResetEmailAsync(user.Email, code);
    }

    public async Task ResetPasswordAsync(string email, string code, string newPassword)
    {
        email = NormalizeEmail(email);
        ValidatePassword(newPassword);

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null || string.IsNullOrWhiteSpace(code) ||
            user.PasswordResetCode != code.Trim() ||
            user.PasswordResetCodeExpiresAt < DateTime.UtcNow)
        {
            throw new AuthException("Недійсний або прострочений код відновлення", StatusCodes.Status400BadRequest);
        }

        user.PasswordResetCode = null;
        user.PasswordResetCodeExpiresAt = null;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

        await dbContext.SaveChangesAsync();
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

    private async Task SendResetEmailAsync(string toEmail, string code)
    {
        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            Credentials = new NetworkCredential(_smtp.Username, _smtp.Password),
            EnableSsl = true
        };

        using var message = new MailMessage(_smtp.FromEmail, toEmail)
        {
            Subject = "Nodus — Код відновлення пароля",
            Body = $"Ваш код для скидання пароля: {code}\nКод дійсний протягом 15 хвилин."
        };

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception)
        {
            throw new AuthException("Не вдалося надіслати лист із кодом відновлення", StatusCodes.Status500InternalServerError);
        }
    }
}
