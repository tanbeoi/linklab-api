namespace LinkLab.Api.Dto;

public record AuthUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? AvatarImageUrl,
    double AvatarFocalPointX,
    double AvatarFocalPointY,
    double AvatarZoom
);
