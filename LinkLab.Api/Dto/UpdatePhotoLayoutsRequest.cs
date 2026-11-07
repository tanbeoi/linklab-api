namespace LinkLab.Api.Dto;

public class UpdatePhotoLayoutsRequest
{
    public List<PhotoLayoutUpdate> Photos { get; set; } = [];
}

public class PhotoLayoutUpdate
{
    public Guid PhotoId { get; set; }
    public double? BoardX { get; set; }
    public double? BoardY { get; set; }
    public double? BoardWidth { get; set; }
    public double? CaptionHeight { get; set; }
    public int? BoardZIndex { get; set; }
}
