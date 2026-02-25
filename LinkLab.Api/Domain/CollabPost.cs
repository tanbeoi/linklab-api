namespace LinkLab.Api.Domain;

public class CollabPost
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Owner
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // Content
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Optional filters (keep v1 simple)
    public string Location { get; set; } = string.Empty; // e.g. "Melbourne" / "Remote"
    public bool IsRemote { get; set; }

    // Timestamps
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}