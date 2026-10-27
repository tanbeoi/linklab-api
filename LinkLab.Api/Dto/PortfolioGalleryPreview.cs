namespace LinkLab.Api.Dto;

public record PortfolioGalleryPreview(
    Guid Id,
    string Title,
    Guid OwnerId,
    string OwnerDisplayName,
    string? PreviewImageUrl,
    int PhotoCount,
    double ThumbnailFocalPointX,
    double ThumbnailFocalPointY,
    double ThumbnailZoom
);
