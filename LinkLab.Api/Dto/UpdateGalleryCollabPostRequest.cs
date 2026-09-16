namespace LinkLab.Api.Dto;
public class UpdateGalleryCollabPostRequest
{
    /// <summary>
    /// The ID of the collaboration post to associate with the gallery.
    /// If null, the association will be removed.
    /// </summary>
    public Guid? CollabPostId { get; set; }
}