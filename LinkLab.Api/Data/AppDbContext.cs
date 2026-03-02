using LinkLab.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LinkLab.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<User> Users => Set<User>();
    public DbSet<CollabPost> CollabPosts => Set<CollabPost>();
    public DbSet<Application> Applications => Set<Application>();

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

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Message)
                .HasMaxLength(2000);

            entity.Property(a => a.Status)
                .IsRequired();

            entity.Property(a => a.CreatedAtUtc)
                .IsRequired();

            entity.HasOne(a => a.Post)
                .WithMany(p => p.Applications)
                .HasForeignKey(a => a.PostId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.ApplicantUser)
                .WithMany()
                .HasForeignKey(a => a.ApplicantUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => new { a.PostId, a.ApplicantUserId })
                .IsUnique();

            entity.HasIndex(a => a.CreatedAtUtc);
        });
    }


}