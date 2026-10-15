namespace LinkLab.Api.Dto;

public class PhotoResponse
{
    public Guid Id { get; set; }
    public Guid GalleryId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public double? BoardX { get; set; }
    public double? BoardY { get; set; }
    public int? BoardZIndex { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
