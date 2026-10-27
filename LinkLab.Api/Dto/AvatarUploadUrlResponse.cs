namespace LinkLab.Api.Dto;

public record AvatarUploadUrlResponse(
    string ObjectKey,
    string UploadUrl,
    string PreviewImageUrl,
    DateTime ExpiresAtUtc
);
