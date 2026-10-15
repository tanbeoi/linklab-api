namespace LinkLab.Api.Dto;

public class UpdatePhotoBoardPositionRequest
{
    public double BoardX { get; set; }
    public double BoardY { get; set; }
    public double? BoardWidth { get; set; }
    public double? CaptionHeight { get; set; }
}
