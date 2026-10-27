namespace LinkLab.Api.Domain; 

public class Gallery
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GalleryPurpose Purpose { get; set; } = GalleryPurpose.Portfolio;
    public bool IsPublished { get; set; } = false;
    public Guid OwnerId { get; set;}

    // null! tells the compiler that this null value will be set later, so it doesn't throw a warning
    public User Owner { get; set; } = null!;
    public Guid? CollabPostId { get; set; }
    public CollabPost? CollabPost { get; set; }
    public List<Photo> Photos { get; set; } = new();
    // A gallery can use either one of its photos or a separately uploaded image as
    // its card thumbnail. When neither value is set, the first gallery photo is used.
    public Guid? ThumbnailPhotoId { get; set; }
    public string? ThumbnailObjectKey { get; set; }
    public double ThumbnailFocalPointX { get; set; } = 50;
    public double ThumbnailFocalPointY { get; set; } = 50;
    public double ThumbnailZoom { get; set; } = 1;
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }

}
