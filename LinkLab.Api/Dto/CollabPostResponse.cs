namespace LinkLab.Api.Dto;

public record CollabPostResponse(
    Guid Id,
    string Title,
    string Description,
    string Location,
    bool IsRemote,
    DateTime CreatedAtUtc,
    Guid UserId,
    string OwnerDisplayName,

    // Moodboard data for frontend display
    IReadOnlyList<string> MoodboardPreviewImageUrls,
    int MoodboardPhotoCount,

    // True only when the authenticated viewer has already applied.
    bool HasCurrentUserApplied
);
