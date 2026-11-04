using Microsoft.EntityFrameworkCore;
using Nodus.Api.Entities;

namespace Nodus.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Наши 4 таблицы в PostgreSQL
    public DbSet<User> Users => Set<User>();
    public DbSet<Roadmap> Roadmaps => Set<Roadmap>();
    public DbSet<RoadmapNode> RoadmapNodes => Set<RoadmapNode>();
    public DbSet<NodeChecklistItem> NodeChecklistItems => Set<NodeChecklistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Nickname)
            .IsUnique();

        modelBuilder.Entity<Roadmap>()
            .HasOne<Roadmap>()
            .WithMany()
            .HasForeignKey(r => r.ClonedFromId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RoadmapNode>()
            .HasOne(n => n.ParentNode)
            .WithMany()
            .HasForeignKey(n => n.ParentNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}