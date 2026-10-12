namespace LinkLab.Api.Dto;

public class AddPhotoToGalleryRequest
{
    public Guid PhotoId { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    public string? Caption { get; set; }

    // When supplied, insert the new photo directly after this photo.
    // Omit it to append the photo to the end of the gallery.
    public Guid? InsertAfterPhotoId { get; set; }

    // Allows the editor to insert a photo before the first existing photo.
    public bool InsertAtStart { get; set; }
}
