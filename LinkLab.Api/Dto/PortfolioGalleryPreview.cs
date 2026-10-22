namespace LinkLab.Api.Dto;

public record PortfolioGalleryPreview(
    Guid Id,
    string Title,
    string OwnerDisplayName,
    string? PreviewImageUrl,
    int PhotoCount
);
