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

        // 1. Почта Email у пользователя должна быть уникальной
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // 2. Правило из Раздела 5.4 спецификации:
        // Если автор удалил оригинальное публичное дерево, у склонированных копий ClonedFromId просто становится NULL
        modelBuilder.Entity<Roadmap>()
            .HasOne<Roadmap>()
            .WithMany()
            .HasForeignKey(r => r.ClonedFromId)
            .OnDelete(DeleteBehavior.SetNull);

        // 3. Связь узла с родительским узлом внутри одной таблицы RoadmapNodes
        modelBuilder.Entity<RoadmapNode>()
            .HasOne(n => n.ParentNode)
            .WithMany()
            .HasForeignKey(n => n.ParentNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}