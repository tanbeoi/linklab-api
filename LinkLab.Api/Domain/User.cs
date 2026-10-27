namespace LinkLab.Api.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = default!;
    public string DisplayName { get; set; } = string.Empty;
    // Avatar images live in the user's own private S3 prefix. The frame values
    // are presentation metadata only; the uploaded source is never modified.
    public string? AvatarObjectKey { get; set; }
    public double AvatarFocalPointX { get; set; } = 50;
    public double AvatarFocalPointY { get; set; } = 50;
    public double AvatarZoom { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
