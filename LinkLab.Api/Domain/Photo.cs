namespace LinkLab.Api.Domain;

public class Photo
{
    public Guid Id { get; set; }
    public Guid GalleryId { get; set; }
    public Gallery Gallery { get; set; } = null!;
    public string ObjectKey { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    // Null means the moodboard photo is still in the owner's Unsorted tray.
    public double? BoardX { get; set; }
    public double? BoardY { get; set; }
    public int? BoardZIndex { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
