namespace LinkLab.Api.Dto;

public record CreateCollabPostRequest(
    string Title,
    string Description,
    string Location,
    bool IsRemote
);