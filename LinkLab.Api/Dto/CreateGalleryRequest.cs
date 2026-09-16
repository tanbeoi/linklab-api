namespace LinkLab.Api.Dto;
using LinkLab.Api.Domain;

public class CreateGalleryRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public GalleryPurpose Purpose { get; set; } = GalleryPurpose.Portfolio;
    public Guid? CollabPostId { get; set; }
    
}
