namespace LinkLab.Api.Dto;

public record UpdateCoverRequest(
    string? ObjectKey,
    double FocalPointX,
    double FocalPointY,
    double Zoom
);
