using LinkLab.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LinkLab.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<User> Users => Set<User>();
    public DbSet<CollabPost> CollabPosts => Set<CollabPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CollabPost>(e =>
        {
            e.Property(p => p.Title).HasMaxLength(100).IsRequired();
            e.Property(p => p.Description).HasMaxLength(2000).IsRequired();
            e.Property(p => p.Location).HasMaxLength(100);

            e.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(p => p.CreatedAtUtc);
        });
    }


}