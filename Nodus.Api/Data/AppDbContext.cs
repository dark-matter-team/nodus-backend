using Microsoft.EntityFrameworkCore;
using Nodus.Api.Entities;

namespace Nodus.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Roadmap> Roadmaps => Set<Roadmap>();
    public DbSet<RoadmapNode> RoadmapNodes => Set<RoadmapNode>();
    public DbSet<NodeChecklistItem> NodeChecklistItems => Set<NodeChecklistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Email).HasMaxLength(FieldLimits.Email);
            user.Property(u => u.Nickname).HasMaxLength(FieldLimits.Nickname);
            user.Property(u => u.GoogleId).HasMaxLength(FieldLimits.GoogleId);
            user.Property(u => u.PasswordResetCode).HasMaxLength(FieldLimits.PasswordResetCode).IsFixedLength();

            user.HasIndex(u => u.Email).IsUnique();

            // Nickname uniqueness is case-insensitive: the unique expression index
            // "IX_Users_Nickname_Lower" ON lower("Nickname") is created by raw SQL in a migration,
            // because EF Core cannot describe expression indexes in the model.
        });

        modelBuilder.Entity<Roadmap>(roadmap =>
        {
            roadmap.Property(r => r.Title).HasMaxLength(FieldLimits.Title);
            roadmap.Property(r => r.GoalPrompt).HasMaxLength(FieldLimits.GoalPrompt);
            roadmap.Property(r => r.TimeCommitment).HasMaxLength(FieldLimits.TimeCommitment);
            roadmap.Property(r => r.KnowledgeLevel).HasConversion<string>().HasMaxLength(FieldLimits.EnumValue);
            roadmap.Property(r => r.Status).HasConversion<string>().HasMaxLength(FieldLimits.EnumValue);

            // Public catalog: only public rows, already sorted the way GetPublicRoadmapsAsync sorts.
            roadmap.HasIndex(r => new { r.ClonesCount, r.CreatedAt })
                .IsDescending(true, true)
                .HasFilter("\"IsPublic\" = true")
                .HasDatabaseName("IX_Roadmaps_Public_ClonesCount_CreatedAt");

            // "My roadmaps": WHERE OwnerId = @id ORDER BY CreatedAt DESC without a separate sort step.
            roadmap.HasIndex(r => new { r.OwnerId, r.CreatedAt })
                .IsDescending(false, true);

            roadmap.HasOne<Roadmap>()
                .WithMany()
                .HasForeignKey(r => r.ClonedFromId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RoadmapNode>(node =>
        {
            node.Property(n => n.Title).HasMaxLength(FieldLimits.Title);
            node.Property(n => n.Category).HasConversion<string>().HasMaxLength(FieldLimits.EnumValue);
            node.Property(n => n.Status).HasConversion<string>().HasMaxLength(FieldLimits.EnumValue);

            // Restrict is intentional: deleting a roadmap/user cascades over all its nodes in one statement
            // (verified on PostgreSQL 16), and NodeService re-links children before deleting a single node.
            node.HasOne(n => n.ParentNode)
                .WithMany()
                .HasForeignKey(n => n.ParentNodeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NodeChecklistItem>()
            .Property(i => i.Title).HasMaxLength(FieldLimits.Title);
    }
}
