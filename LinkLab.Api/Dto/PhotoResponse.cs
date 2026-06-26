namespace LinkLab.Api.Dto;

public class PhotoResponse
{
    public Guid Id { get; set; }
    public Guid GalleryId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}