namespace LinkLab.Api.Dto;

public record CollabPostResponse(
    Guid Id,
    string Title,
    string Description,
    string Location,
    bool IsRemote,
    bool IsClosed,
    DateTime CreatedAtUtc,
    Guid UserId,
    string OwnerDisplayName,

    // Moodboard data for frontend display
    IReadOnlyList<string> MoodboardPreviewImageUrls,
    int MoodboardPhotoCount,

    // Published portfolio work made through this post.
    IReadOnlyList<PortfolioGalleryPreview> PortfolioGalleries,

    // True only when the authenticated viewer has already applied.
    bool HasCurrentUserApplied,

    // The authenticated viewer's current application status, when they have applied.
    // This lets post views show a meaningful outcome instead of only "Applied".
    string? CurrentUserApplicationStatus
);
