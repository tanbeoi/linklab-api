namespace LinkLab.Api.Dto;

public record MyApplicationResponse(
    Guid Id,
    Guid PostId,
    string PostTitle,
    string Message,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? DecidedAtUtc
);
