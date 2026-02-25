namespace LinkLab.Api.Dto;

public record CollabPostResponse(
    Guid Id,
    string Title,
    string Description,
    string Location,
    bool IsRemote,
    DateTime CreatedAtUtc,
    Guid UserId,
    string OwnerDisplayName
);