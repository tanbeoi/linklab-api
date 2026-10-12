using LinkLab.Api.Domain;

namespace LinkLab.Api.Dto;

public class UpdateGalleryRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public GalleryPurpose Purpose { get; set; }
}
