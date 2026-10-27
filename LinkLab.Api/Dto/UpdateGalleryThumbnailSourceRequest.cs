namespace LinkLab.Api.Dto;

public class UpdateGalleryThumbnailSourceRequest
{
    // A gallery photo to use as the thumbnail. Leave both fields empty to return
    // to the automatic first-photo thumbnail.
    public Guid? PhotoId { get; set; }

    // A separately uploaded thumbnail under this gallery's thumbnails/ prefix.
    public string? ObjectKey { get; set; }
}
