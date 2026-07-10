namespace LinkLab.Api.Dto;

public class AddPhotoToGalleryRequest
{
    public Guid PhotoId { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    public string? Caption { get; set; }
}