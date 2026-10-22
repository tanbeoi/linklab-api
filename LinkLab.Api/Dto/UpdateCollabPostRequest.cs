namespace LinkLab.Api.Dto;

public record UpdateCollabPostRequest(
    string Title,
    string Description,
    string Location,
    bool IsRemote
);
