namespace LinkLab.Api.Dto;

public class GalleryThumbnailResponse
{
    public Guid? ThumbnailPhotoId { get; set; }
    public bool HasCustomThumbnail { get; set; }
    public string? PreviewImageUrl { get; set; }
    public double ThumbnailFocalPointX { get; set; }
    public double ThumbnailFocalPointY { get; set; }
    public double ThumbnailZoom { get; set; }
}
