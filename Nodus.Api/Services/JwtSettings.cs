namespace Nodus.Api.Services;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;   // мінімум 32 символи
    public string Issuer { get; set; } = "Nodus.Api";
    public string Audience { get; set; } = "Nodus.Client";
    public int ExpiresMinutes { get; set; } = 60 * 24 * 7; // 7 днів
}
