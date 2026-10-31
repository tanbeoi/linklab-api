using System.ComponentModel.DataAnnotations;

namespace LinkLab.Api.Dto;

public class AddYoutubeVideoRequest
{
    [Required, MaxLength(10000)]
    public string EmbedCode { get; set; } = string.Empty;
    public Guid? InsertAfterPhotoId { get; set; }
    public bool InsertAtStart { get; set; }
}
