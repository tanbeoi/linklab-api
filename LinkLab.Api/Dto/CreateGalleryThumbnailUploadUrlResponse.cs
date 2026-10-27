namespace LinkLab.Api.Dto;

public class CreateGalleryThumbnailUploadUrlResponse
{
    public string ObjectKey { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
    public string PreviewImageUrl { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
