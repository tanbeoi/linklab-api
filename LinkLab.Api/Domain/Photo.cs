namespace LinkLab.Api.Domain;

public class Photo
{
    public Guid Id { get; set; }
    public Guid GalleryId { get; set; }
    public Gallery Gallery { get; set; } = null!;
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}