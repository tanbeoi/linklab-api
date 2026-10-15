namespace LinkLab.Api.Dto;

public class UpdatePhotoCaptionsRequest
{
    public List<UpdatePhotoCaptionRequest> Captions { get; set; } = new();
}

public class UpdatePhotoCaptionRequest
{
    public Guid PhotoId { get; set; }
    public string? Caption { get; set; }
}
