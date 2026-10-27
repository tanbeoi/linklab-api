namespace LinkLab.Api.Dto;

public record CoverUploadUrlResponse(
    string ObjectKey,
    string UploadUrl,
    string PreviewImageUrl,
    DateTime ExpiresAtUtc
);
