using LinkLab.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LinkLab.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<User> Users => Set<User>();
    public DbSet<CollabPost> CollabPosts => Set<CollabPost>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Gallery> Galleries => Set<Gallery>();
    public DbSet<Photo> Photos => Set<Photo>();

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

        modelBuilder.Entity<Gallery>(entity =>
        {
            entity.HasKey(g => g.Id);

            entity.Property(g => g.Title)
                .IsRequired();

            entity.Property(g => g.Description)
                .HasMaxLength(2000);

            entity.HasIndex(g => new { g.OwnerId, g.SortOrder });

            entity.Property(g => g.CreatedAtUtc)
                .IsRequired();

            entity.HasOne(g => g.Owner)
                .WithMany()
                .HasForeignKey(g => g.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(g => g.CollabPost)
                .WithMany()
                .HasForeignKey(g => g.CollabPostId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Photo>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.ImageUrl)
                .IsRequired();

            entity.Property(p => p.Caption)
                .HasMaxLength(500);

            entity.Property(p => p.SortOrder)
                .IsRequired();

            entity.Property(p => p.CreatedAtUtc)
                .IsRequired();

            entity.HasOne(p => p.Gallery)
                .WithMany(g => g.Photos)
                .HasForeignKey(p => p.GalleryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Each photos in a gallery must have an unique SortOrder
            entity.HasIndex(p => new { p.GalleryId, p.SortOrder })
                .IsUnique();
        });
    }


}