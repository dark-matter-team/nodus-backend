namespace Nodus.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Roadmap> Roadmaps { get; set; } = new();
    public string? PasswordResetCode { get; set; }
    public DateTime? PasswordResetCodeExpiresAt { get; set; }

    /// <summary>Put into every JWT; incremented to invalidate all previously issued tokens.</summary>
    public int TokenVersion { get; set; }
}