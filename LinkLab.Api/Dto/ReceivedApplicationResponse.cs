namespace LinkLab.Api.Dto;

public record ReceivedApplicationResponse(
    Guid Id,
    Guid PostId,
    string PostTitle,
    Guid ApplicantUserId,
    string ApplicantDisplayName,
    string Message,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? DecidedAtUtc
);
