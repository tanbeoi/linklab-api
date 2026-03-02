namespace LinkLab.Api.Dto;

public class ApplicationListItemResponse
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid ApplicantUserId { get; set; }
    public string ApplicantEmail { get; set; } = string.Empty;
    public string ApplicantDisplayName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}