namespace LinkLab.Api.Dto;

public class CreatePhotoUploadUrlResponse
{
    public Guid PhotoId { get; set; }
    public Guid GalleryId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

