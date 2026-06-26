namespace LinkLab.Api.Dto;

public class CreateGalleryRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public Guid? CollabPostId { get; set; }
}
