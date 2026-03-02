namespace LinkLab.Api.Domain;

public class Application
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PostId { get; set; }
    public CollabPost Post { get; set; } = null!;

    public Guid ApplicantUserId { get; set; }
    public User ApplicantUser { get; set; } = null!;

    public string Message { get; set; } = string.Empty;

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DecidedAtUtc { get; set; }
}