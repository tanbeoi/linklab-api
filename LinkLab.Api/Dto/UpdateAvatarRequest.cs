namespace LinkLab.Api.Dto;

public record UpdateAvatarRequest(
    string? ObjectKey,
    double FocalPointX,
    double FocalPointY,
    double Zoom
);
