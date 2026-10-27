using System.Security.Claims;
using LinkLab.Api.Data;
using LinkLab.Api.Domain;
using LinkLab.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

using Amazon.S3;
using Amazon.S3.Model;
using LinkLab.Api.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/galleries")]
public class GalleriesController : ControllerBase
{
    private const double MoodboardCanvasWidth = 2400;
    private const double MoodboardCanvasHeight = 1600;
    private readonly AppDbContext _db;
    private readonly IAmazonS3 _s3;
    private readonly S3Options _s3Options;

    // Helper method to validate if the user can link a gallery to a collaboration post
    private async Task<IActionResult?> ValidateCollabPostLinkAsync(
        Guid userId,
        Guid collabPostId,
        GalleryPurpose purpose,
        Guid? currentGalleryId = null)
    {
        var post = await _db.CollabPosts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == collabPostId);

        if (post is null)
            return BadRequest(new { error = "Collab post does not exist." });

        var isPostOwner = post.UserId == userId;

        if (!isPostOwner)
        {
            var isAcceptedCollaborator = await _db.Applications
                .AsNoTracking()
                .AnyAsync(a =>
                    a.PostId == collabPostId &&
                    a.ApplicantUserId == userId &&
                    a.Status == ApplicationStatus.Accepted);

            if (!isAcceptedCollaborator)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "Only the post creator or accepted collaborators can link a gallery to this post."
                });
            }
        }

        if (purpose == GalleryPurpose.Moodboard)
        {
            var alreadyLinked = await _db.Galleries
                .AsNoTracking()
                .AnyAsync(g => g.CollabPostId == collabPostId &&
                    g.Purpose == GalleryPurpose.Moodboard &&
                    // 1. Creating: currentGalleryId is null, so check all moodboards.
                    // 2. Updating: exclude the current gallery and check whether
                    //    another moodboard is already linked to the target post.
                    (!currentGalleryId.HasValue || g.Id != currentGalleryId.Value));

            if (alreadyLinked)
                return Conflict(new { error = "This post already has a linked moodboard. Unlink it before linking another." });
        }

        return null; // No error: linking is allowed.
    }

    private async Task<IActionResult?> SaveGalleryChangesAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: AppDbContext.MoodboardPostIndexName
            })
        {
            return Conflict(new { error = "This post already has a linked moodboard. Unlink it before linking another." });
        }
    }

    public GalleriesController(AppDbContext db, IAmazonS3 s3, IOptions<S3Options> s3Options)
    {
        _db = db;
        _s3 = s3;
        _s3Options = s3Options.Value;
    }

    private string CreateReadUrl(string objectKey, DateTime expiresAtUtc)
        => _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _s3Options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expiresAtUtc
        });

    private async Task<string?> GetThumbnailObjectKeyAsync(Gallery gallery)
    {
        if (!string.IsNullOrWhiteSpace(gallery.ThumbnailObjectKey))
            return gallery.ThumbnailObjectKey;

        var photos = _db.Photos.AsNoTracking().Where(photo => photo.GalleryId == gallery.Id);

        if (gallery.ThumbnailPhotoId.HasValue)
        {
            var selectedPhotoKey = await photos
                .Where(photo => photo.Id == gallery.ThumbnailPhotoId.Value)
                .Select(photo => photo.ObjectKey)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(selectedPhotoKey))
                return selectedPhotoKey;
        }

        return await photos
            .OrderBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAtUtc)
            .ThenBy(photo => photo.Id)
            .Select(photo => photo.ObjectKey)
            .FirstOrDefaultAsync();
    }

    private async Task<GalleryThumbnailResponse> CreateThumbnailResponseAsync(Gallery gallery)
    {
        var objectKey = await GetThumbnailObjectKeyAsync(gallery);

        return new GalleryThumbnailResponse
        {
            ThumbnailPhotoId = gallery.ThumbnailPhotoId,
            HasCustomThumbnail = !string.IsNullOrWhiteSpace(gallery.ThumbnailObjectKey),
            PreviewImageUrl = string.IsNullOrWhiteSpace(objectKey)
                ? null
                : CreateReadUrl(objectKey, DateTime.UtcNow.AddMinutes(30)),
            ThumbnailFocalPointX = gallery.ThumbnailFocalPointX,
            ThumbnailFocalPointY = gallery.ThumbnailFocalPointY,
            ThumbnailZoom = gallery.ThumbnailZoom
        };
    }

    private async Task AddPreviewImagesAsync(IReadOnlyList<GalleryResponse> galleries)
    {
        if (galleries.Count == 0)
            return;

        var galleryIds = galleries.Select(gallery => gallery.Id).ToList();

        // A custom thumbnail wins, then an explicitly selected gallery photo, then
        // the gallery's first photo. Fetch every source in one query for card lists.
        var previewSources = await _db.Galleries
            .AsNoTracking()
            .Where(gallery => galleryIds.Contains(gallery.Id))
            .Select(gallery => new
            {
                gallery.Id,
                gallery.ThumbnailObjectKey,
                SelectedPhotoObjectKey = gallery.ThumbnailPhotoId.HasValue
                    ? gallery.Photos
                        .Where(photo => photo.Id == gallery.ThumbnailPhotoId.Value)
                        .Select(photo => photo.ObjectKey)
                        .FirstOrDefault()
                    : null,
                FirstPhotoObjectKey = gallery.Photos
                    .OrderBy(photo => photo.SortOrder)
                    .ThenBy(photo => photo.CreatedAtUtc)
                    .ThenBy(photo => photo.Id)
                    .Select(photo => photo.ObjectKey)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var objectKeysByGalleryId = previewSources
            .Select(source => new
            {
                source.Id,
                ObjectKey = source.ThumbnailObjectKey ?? source.SelectedPhotoObjectKey ?? source.FirstPhotoObjectKey
            })
            .Where(source => !string.IsNullOrWhiteSpace(source.ObjectKey))
            .ToDictionary(source => source.Id, source => source.ObjectKey!);

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(30);

        foreach (var gallery in galleries)
        {
            if (!objectKeysByGalleryId.TryGetValue(gallery.Id, out var objectKey))
                continue;

            gallery.PreviewImageUrl = CreateReadUrl(objectKey, expiresAtUtc);
        }
    }

    // [x] Create gallery (auth required)
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(CreateGalleryRequest req)
    {
        // Find current user from JWT
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // Basic validation (v1)
        var title = (req.Title ?? string.Empty).Trim();
        var desc = (req.Description ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title))
            title = "Untitled";

        if (title.Length > 150)
            return BadRequest(new { error = "Title must be 150 characters or less." });

        if (desc.Length > 2000)
            return BadRequest(new { error = "Description must be 2000 characters or less." });

        if (!Enum.IsDefined(req.Purpose))
        {
            return BadRequest(new
            {
                error = "Invalid gallery purpose."
            });
        }

        // If CollabPostId is provided, verify the user's connection to it using the ValidateCollabPostLinkAsync helper
        if (req.CollabPostId.HasValue)
        {
            var error = await ValidateCollabPostLinkAsync(
                userId,
                req.CollabPostId.Value,
                req.Purpose);

            if (error is not null)
                return error;
        }

        // Find the highest sortOrder this user alr has, if no galleries then use -1
        var nextSortOrder = await _db.Galleries
            .Where(g => g.OwnerId == userId)
            .Select(g => (int?)g.SortOrder)
            .MaxAsync() ?? -1;

        // The pattern is:
        // 1. Build entity
        var gallery = new Gallery
        {
            OwnerId = userId,
            Title = title,
            Description = string.IsNullOrWhiteSpace(desc) ? null : desc,
            Purpose = req.Purpose,
            CollabPostId = req.CollabPostId,
            SortOrder = nextSortOrder + 1,
            CreatedAtUtc = DateTime.UtcNow
        };

        // 2. Save gallery to database
        _db.Galleries.Add(gallery);
        var saveError = await SaveGalleryChangesAsync();
        if (saveError is not null)
            return saveError;

        // 3. Build response
        var res = new GalleryResponse
        {
            Id = gallery.Id,
            Title = gallery.Title,
            Description = gallery.Description,
            Purpose = gallery.Purpose,
            OwnerId = gallery.OwnerId,
            IsPublished = gallery.IsPublished,
            CollabPostId = gallery.CollabPostId,
            CollabPostTitle = null,
            SortOrder = gallery.SortOrder,
            CreatedAtUtc = gallery.CreatedAtUtc,
            PublishedAtUtc = gallery.PublishedAtUtc,
            PhotoCount = 0,
            ThumbnailPhotoId = gallery.ThumbnailPhotoId,
            HasCustomThumbnail = !string.IsNullOrWhiteSpace(gallery.ThumbnailObjectKey),
            ThumbnailFocalPointX = gallery.ThumbnailFocalPointX,
            ThumbnailFocalPointY = gallery.ThumbnailFocalPointY,
            ThumbnailZoom = gallery.ThumbnailZoom
        };

        // Return 201 Created,
        // tell the client where the new gallery lives,
        // and include the created gallery data in the response body.
        return Created($"/api/galleries/{gallery.Id}", res);
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> ListMine()
    {
        // Find current user from JWT
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // Get galleries for the current user
        var galleries = await _db.Galleries
            .AsNoTracking()
            .Where(g => g.OwnerId == userId)
            .OrderByDescending(g => g.CreatedAtUtc)
            .Select(g => new GalleryResponse
            {
                Id = g.Id,
                Title = g.Title,
                Description = g.Description,
                Purpose = g.Purpose,
                OwnerId = g.OwnerId,
                IsPublished = g.IsPublished,
                CollabPostId = g.CollabPostId,
                CollabPostTitle = g.CollabPost == null ? null : g.CollabPost.Title,
                SortOrder = g.SortOrder,
                CreatedAtUtc = g.CreatedAtUtc,
                PublishedAtUtc = g.PublishedAtUtc,
                PhotoCount = g.Photos.Count,
                OwnerDisplayName = g.Owner.DisplayName,
                ThumbnailPhotoId = g.ThumbnailPhotoId,
                HasCustomThumbnail = g.ThumbnailObjectKey != null,
                ThumbnailFocalPointX = g.ThumbnailFocalPointX,
                ThumbnailFocalPointY = g.ThumbnailFocalPointY,
                ThumbnailZoom = g.ThumbnailZoom
            })
            .ToListAsync();

        await AddPreviewImagesAsync(galleries);

        return Ok(galleries);
    }

    // Read one gallery. Published galleries are public; draft galleries are visible
    // only to their owner. The editor uses this endpoint for its initial details.
    [AllowAnonymous]
    [HttpGet("{galleryId:guid}")]
    public async Task<IActionResult> Get(Guid galleryId)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? currentUserId = Guid.TryParse(userIdText, out var userId)
            ? userId
            : null;

        var gallery = await _db.Galleries
            .AsNoTracking()
            .Where(g => g.Id == galleryId)
            .Select(g => new GalleryResponse
            {
                Id = g.Id,
                Title = g.Title,
                Description = g.Description,
                Purpose = g.Purpose,
                OwnerId = g.OwnerId,
                IsPublished = g.IsPublished,
                CollabPostId = g.CollabPostId,
                CollabPostTitle = g.CollabPost == null ? null : g.CollabPost.Title,
                SortOrder = g.SortOrder,
                CreatedAtUtc = g.CreatedAtUtc,
                PublishedAtUtc = g.PublishedAtUtc,
                PhotoCount = g.Photos.Count,
                OwnerDisplayName = g.Owner.DisplayName,
                ThumbnailPhotoId = g.ThumbnailPhotoId,
                HasCustomThumbnail = g.ThumbnailObjectKey != null,
                ThumbnailFocalPointX = g.ThumbnailFocalPointX,
                ThumbnailFocalPointY = g.ThumbnailFocalPointY,
                ThumbnailZoom = g.ThumbnailZoom
            })
            .FirstOrDefaultAsync();

        if (gallery is null || (!gallery.IsPublished && gallery.OwnerId != currentUserId))
            return NotFound(new { error = "Gallery not found." });

        await AddPreviewImagesAsync(new[] { gallery });
        return Ok(gallery);
    }

    [Authorize]
    [HttpPut("{galleryId:guid}")]
    public async Task<IActionResult> Update(Guid galleryId, UpdateGalleryRequest req)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        var gallery = await _db.Galleries
            .FirstOrDefaultAsync(g => g.Id == galleryId && g.OwnerId == userId);

        if (gallery is null)
            return NotFound(new { error = "Gallery not found." });

        var title = (req.Title ?? string.Empty).Trim();
        var description = (req.Description ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { error = "Gallery title is required." });

        if (title.Length > 150)
            return BadRequest(new { error = "Title must be 150 characters or less." });

        if (description.Length > 2000)
            return BadRequest(new { error = "Description must be 2000 characters or less." });

        if (!Enum.IsDefined(req.Purpose))
            return BadRequest(new { error = "Invalid gallery purpose." });

        if (gallery.CollabPostId.HasValue)
        {
            var linkError = await ValidateCollabPostLinkAsync(
                userId,
                gallery.CollabPostId.Value,
                req.Purpose,
                gallery.Id);

            if (linkError is not null)
                return linkError;
        }

        gallery.Title = title;
        gallery.Description = string.IsNullOrWhiteSpace(description) ? null : description;
        gallery.Purpose = req.Purpose;

        var saveError = await SaveGalleryChangesAsync();
        if (saveError is not null)
            return saveError;

        return Ok(new
        {
            gallery.Id,
            gallery.Title,
            gallery.Description,
            gallery.Purpose
        });
    }

    // List all public galleries (public)
    [HttpGet]
    [ProducesResponseType(
        typeof(PagedResponse<GalleryResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult>List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? ownerId = null)
    {
        // 1. Validate pagination values
        if (page < 1)
        {
            return BadRequest(new
            {
                error = "Page must be at least 1."
            });
        }

        if (pageSize < 1 || pageSize > 50)
        {
            return BadRequest(new
            {
                error = "Page size must be between 1 and 50."
            });
        }

        // 2. Build the base query for all public galleries
        var query = _db.Galleries
            .AsNoTracking()
            .Where(g => g.IsPublished && (!ownerId.HasValue || g.OwnerId == ownerId.Value));

        // 3. Count all matching posts
        var totalCount = await query.CountAsync();

        // 4. Calculate how many posts to skip
        var skip = (page - 1) * pageSize;

        // 5. Load only the requested page
        var galleries = await query
            .OrderByDescending(g => g.PublishedAtUtc)
            .ThenByDescending(g => g.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(g => new GalleryResponse
            {
                Id = g.Id,
                Title = g.Title,
                Description = g.Description,
                Purpose = g.Purpose,
                IsPublished = g.IsPublished,
                OwnerId = g.OwnerId,
                CollabPostId = g.CollabPostId,
                CollabPostTitle = g.CollabPost == null ? null : g.CollabPost.Title,
                SortOrder = g.SortOrder,
                CreatedAtUtc = g.CreatedAtUtc,
                PublishedAtUtc = g.PublishedAtUtc,
                PhotoCount = g.Photos.Count,
                OwnerDisplayName = g.Owner.DisplayName,
                ThumbnailPhotoId = g.ThumbnailPhotoId,
                HasCustomThumbnail = g.ThumbnailObjectKey != null,
                ThumbnailFocalPointX = g.ThumbnailFocalPointX,
                ThumbnailFocalPointY = g.ThumbnailFocalPointY,
                ThumbnailZoom = g.ThumbnailZoom
            })
            .ToListAsync();

        await AddPreviewImagesAsync(galleries);

        // 6. Calculate total pages
        var totalPages = (int)Math.Ceiling(
            totalCount / (double)pageSize
        );

        // 7. Return galleries and pagination information
        return Ok(new PagedResponse<GalleryResponse>
        {
            Items = galleries,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasPreviousPage = page > 1,
            HasNextPage = page < totalPages
        });
    }

    [AllowAnonymous]
    [HttpGet("{galleryId:guid}/photos")]
    public async Task<IActionResult> ListPhotos(Guid galleryId)
        {
        // The request may be authenticated or anonymous
        var userIdText =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        Guid? currentUserId = null;

        if (Guid.TryParse(userIdText, out var parsedUserId))
            currentUserId = parsedUserId;

        // Find the gallery and read its visibility
        var gallery = await _db.Galleries
            .AsNoTracking()
            .Where(g => g.Id == galleryId)
            .Select(g => new
            {
                g.Id,
                g.OwnerId,
                g.IsPublished
            })
            .FirstOrDefaultAsync();

        if (gallery is null)
            return NotFound(new { error = "Gallery not found." });

        var isOwner =
            currentUserId.HasValue &&
            gallery.OwnerId == currentUserId.Value;

        // Private galleries are only visible to their owner
        if (!gallery.IsPublished && !isOwner)
            return NotFound(new { error = "Gallery not found." });

        var photos = await _db.Photos
            .AsNoTracking()
            .Where(p => p.GalleryId == galleryId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id)
            .ToListAsync();

        var res = photos.Select(p => new PhotoResponse
        {
            Id = p.Id,
            GalleryId = p.GalleryId,
            ObjectKey = p.ObjectKey,
            ImageUrl = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _s3Options.BucketName,
                Key = p.ObjectKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(30)
            }),
            Caption = p.Caption,
            SortOrder = p.SortOrder,
            BoardX = p.BoardX,
            BoardY = p.BoardY,
            BoardWidth = p.BoardWidth,
            CaptionHeight = p.CaptionHeight,
            BoardZIndex = p.BoardZIndex,
            CreatedAtUtc = p.CreatedAtUtc
        });

        return Ok(res);
    }

    [Authorize]
    [HttpPatch("{galleryId:guid}/photos/{photoId:guid}/board-position")]
    public async Task<IActionResult> UpdatePhotoBoardPosition(
        Guid galleryId,
        Guid photoId,
        UpdatePhotoBoardPositionRequest req)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        if (!double.IsFinite(req.BoardX) || !double.IsFinite(req.BoardY) ||
            req.BoardX < 0 || req.BoardX > MoodboardCanvasWidth ||
            req.BoardY < 0 || req.BoardY > MoodboardCanvasHeight)
        {
            return BadRequest(new { error = "The photo position is outside the moodboard canvas." });
        }

        if (req.BoardWidth.HasValue &&
            (!double.IsFinite(req.BoardWidth.Value) || req.BoardWidth.Value < 120 || req.BoardWidth.Value > MoodboardCanvasWidth))
        {
            return BadRequest(new { error = "The image width must fit within the moodboard canvas." });
        }

        if (req.CaptionHeight.HasValue &&
            (!double.IsFinite(req.CaptionHeight.Value) || req.CaptionHeight.Value < 40 || req.CaptionHeight.Value > 320))
        {
            return BadRequest(new { error = "The caption height must be between 40 and 320 pixels." });
        }

        var gallery = await _db.Galleries
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == galleryId && g.OwnerId == userId);

        if (gallery is null)
            return NotFound(new { error = "Gallery not found." });

        if (gallery.Purpose != GalleryPurpose.Moodboard)
            return BadRequest(new { error = "Only moodboard photos can be positioned on a board." });

        var photo = await _db.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.GalleryId == galleryId);

        if (photo is null)
            return NotFound(new { error = "Photo not found." });

        var nextZIndex = await _db.Photos
            .Where(p => p.GalleryId == galleryId)
            .MaxAsync(p => (int?)p.BoardZIndex) ?? 0;

        photo.BoardX = req.BoardX;
        photo.BoardY = req.BoardY;
        if (req.BoardWidth.HasValue)
            photo.BoardWidth = req.BoardWidth.Value;
        if (req.CaptionHeight.HasValue)
            photo.CaptionHeight = req.CaptionHeight.Value;
        photo.BoardZIndex = nextZIndex + 1;
        await _db.SaveChangesAsync();

        return Ok(new PhotoResponse
        {
            Id = photo.Id,
            GalleryId = photo.GalleryId,
            ObjectKey = photo.ObjectKey,
            ImageUrl = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _s3Options.BucketName,
                Key = photo.ObjectKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(30)
            }),
            Caption = photo.Caption,
            SortOrder = photo.SortOrder,
            BoardX = photo.BoardX,
            BoardY = photo.BoardY,
            BoardWidth = photo.BoardWidth,
            CaptionHeight = photo.CaptionHeight,
            BoardZIndex = photo.BoardZIndex,
            CreatedAtUtc = photo.CreatedAtUtc
        });
    }

    [Authorize]
    [HttpPatch("{galleryId:guid}/photos/captions")]
    public async Task<IActionResult> UpdatePhotoCaptions(
        Guid galleryId,
        UpdatePhotoCaptionsRequest req)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        if (req.Captions.Count == 0)
            return NoContent();

        if (req.Captions.Any(caption => caption.PhotoId == Guid.Empty) ||
            req.Captions.Select(caption => caption.PhotoId).Distinct().Count() != req.Captions.Count)
        {
            return BadRequest(new { error = "Each photo can only be updated once." });
        }

        if (req.Captions.Any(caption => caption.Caption?.Length > 500))
            return BadRequest(new { error = "Photo captions must be 500 characters or fewer." });

        var ownsGallery = await _db.Galleries
            .AsNoTracking()
            .AnyAsync(gallery => gallery.Id == galleryId && gallery.OwnerId == userId);

        if (!ownsGallery)
            return NotFound(new { error = "Gallery not found or does not belong to the current user." });

        var requestedPhotoIds = req.Captions.Select(caption => caption.PhotoId).ToList();
        var photos = await _db.Photos
            .Where(photo => photo.GalleryId == galleryId && requestedPhotoIds.Contains(photo.Id))
            .ToListAsync();

        if (photos.Count != requestedPhotoIds.Count)
            return BadRequest(new { error = "One or more photos do not belong to this gallery." });

        var captionsByPhotoId = req.Captions.ToDictionary(caption => caption.PhotoId, caption => caption.Caption);
        foreach (var photo in photos)
        {
            var caption = captionsByPhotoId[photo.Id]?.Trim();
            photo.Caption = string.IsNullOrWhiteSpace(caption) ? null : caption;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Permanently remove a photo the current user owns. This also removes the
    // original object from S3 so deleted moodboard cards do not leave orphaned files.
    [Authorize]
    [HttpDelete("{galleryId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> DeletePhoto(Guid galleryId, Guid photoId)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        var photo = await _db.Photos
            .Include(candidate => candidate.Gallery)
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == photoId &&
                candidate.GalleryId == galleryId &&
                candidate.Gallery.OwnerId == userId);

        if (photo is null)
            return NotFound(new { error = "Photo not found." });

        if (photo.Gallery.ThumbnailPhotoId == photo.Id)
        {
            photo.Gallery.ThumbnailPhotoId = null;
            photo.Gallery.ThumbnailFocalPointX = 50;
            photo.Gallery.ThumbnailFocalPointY = 50;
            photo.Gallery.ThumbnailZoom = 1;
        }

        await _s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _s3Options.BucketName,
            Key = photo.ObjectKey
        }, HttpContext.RequestAborted);

        _db.Photos.Remove(photo);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // [x] Create photo upload URL (auth required)
    [Authorize]
    [HttpPost("{galleryId:guid}/photos/upload-url")]
    public async Task<IActionResult> CreatePhotoUploadUrl(
        Guid galleryId,
        CreatePhotoUploadUrlRequest req)
    {
        // 1. Find current user from JWT
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // 2. Check if the gallery exists and belongs to the current user
        var galleryExists = await _db.Galleries
            .AsNoTracking()
            .AnyAsync(g => g.Id == galleryId && g.OwnerId == userId);

        if (!galleryExists)
            return NotFound(new { error = "Gallery not found or does not belong to the current user." });

        // 3. Validate the image type
        var contentType = (req.ContentType ?? string.Empty).Trim().ToLowerInvariant();

        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            return BadRequest(new { error = "Only jpeg, png, and webp images are allowed." });

        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ""
        };

        // 4. Generate a new photo ID and S3 object key
        var photoId = Guid.NewGuid();

        var objectKey =
            $"users/{userId}/galleries/{galleryId}/photos/{photoId}{extension}";

        // 5. Generate a pre-signed URL for uploading the photo to S3
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(10);

        var urlRequest = new GetPreSignedUrlRequest
        {
            BucketName = _s3Options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAtUtc,
            ContentType = contentType 
        };

        var uploadUrl = _s3.GetPreSignedURL(urlRequest);

        // 6. Return the upload URL and photo ID to the client
        var res = new CreatePhotoUploadUrlResponse
        {
            PhotoId = photoId,
            GalleryId = galleryId,
            ObjectKey = objectKey,
            UploadUrl = uploadUrl,
            ExpiresAtUtc = expiresAtUtc
        };

        return Ok(res);
    }

    // Create a signed S3 upload URL for an optional, gallery-card-only thumbnail.
    // Unlike regular photos, this image is not added to the gallery's photo list.
    [Authorize]
    [HttpPost("{galleryId:guid}/thumbnail/upload-url")]
    public async Task<IActionResult> CreateThumbnailUploadUrl(
        Guid galleryId,
        CreatePhotoUploadUrlRequest req)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdText) || !Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        var ownsGallery = await _db.Galleries
            .AsNoTracking()
            .AnyAsync(gallery => gallery.Id == galleryId && gallery.OwnerId == userId);

        if (!ownsGallery)
            return NotFound(new { error = "Gallery not found or does not belong to the current user." });

        var contentType = (req.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(extension))
            return BadRequest(new { error = "Only jpeg, png, and webp images are allowed." });

        var objectKey = $"users/{userId}/galleries/{galleryId}/thumbnails/{Guid.NewGuid()}{extension}";
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(10);
        var uploadUrl = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _s3Options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAtUtc,
            ContentType = contentType
        });

        return Ok(new CreateGalleryThumbnailUploadUrlResponse
        {
            ObjectKey = objectKey,
            UploadUrl = uploadUrl,
            PreviewImageUrl = CreateReadUrl(objectKey, expiresAtUtc),
            ExpiresAtUtc = expiresAtUtc
        });
    }

    [Authorize]
    [HttpPatch("{galleryId:guid}/thumbnail")]
    public async Task<IActionResult> UpdateThumbnailSource(
        Guid galleryId,
        UpdateGalleryThumbnailSourceRequest req)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdText) || !Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        var suppliedObjectKey = (req.ObjectKey ?? string.Empty).Trim();
        if (req.PhotoId.HasValue && !string.IsNullOrEmpty(suppliedObjectKey))
            return BadRequest(new { error = "Choose either a gallery photo or an uploaded thumbnail." });

        var gallery = await _db.Galleries
            .FirstOrDefaultAsync(candidate => candidate.Id == galleryId && candidate.OwnerId == userId);

        if (gallery is null)
            return NotFound(new { error = "Gallery not found or does not belong to the current user." });

        if (req.PhotoId.HasValue)
        {
            var photoBelongsToGallery = await _db.Photos
                .AsNoTracking()
                .AnyAsync(photo => photo.Id == req.PhotoId.Value && photo.GalleryId == galleryId);

            if (!photoBelongsToGallery)
                return BadRequest(new { error = "The selected photo does not belong to this gallery." });
        }

        if (!string.IsNullOrEmpty(suppliedObjectKey))
        {
            var expectedPrefix = $"users/{userId}/galleries/{galleryId}/thumbnails/";
            if (!suppliedObjectKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
                return BadRequest(new { error = "The thumbnail image does not belong to this gallery." });

            try
            {
                var metadata = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _s3Options.BucketName,
                    Key = suppliedObjectKey
                }, HttpContext.RequestAborted);

                if (metadata.ContentLength == 0)
                    return BadRequest(new { error = "The uploaded thumbnail is empty." });
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return BadRequest(new { error = "The thumbnail has not been uploaded to S3." });
            }
        }

        var previousCustomObjectKey = gallery.ThumbnailObjectKey;
        gallery.ThumbnailPhotoId = req.PhotoId;
        gallery.ThumbnailObjectKey = string.IsNullOrEmpty(suppliedObjectKey) ? null : suppliedObjectKey;
        gallery.ThumbnailFocalPointX = 50;
        gallery.ThumbnailFocalPointY = 50;
        gallery.ThumbnailZoom = 1;
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(previousCustomObjectKey) &&
            !string.Equals(previousCustomObjectKey, gallery.ThumbnailObjectKey, StringComparison.Ordinal))
        {
            try
            {
                await _s3.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _s3Options.BucketName,
                    Key = previousCustomObjectKey
                }, HttpContext.RequestAborted);
            }
            catch (AmazonS3Exception)
            {
                // The gallery now uses its new source. A failed cleanup must not
                // make the update appear to fail to the gallery owner.
            }
        }

        return Ok(await CreateThumbnailResponseAsync(gallery));
    }

    [Authorize]
    [HttpPatch("{galleryId:guid}/thumbnail/frame")]
    public async Task<IActionResult> UpdateThumbnailFrame(
        Guid galleryId,
        UpdateGalleryThumbnailFrameRequest req)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdText) || !Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        if (!double.IsFinite(req.FocalPointX) || !double.IsFinite(req.FocalPointY) || !double.IsFinite(req.Zoom) ||
            req.FocalPointX is < 0 or > 100 || req.FocalPointY is < 0 or > 100 || req.Zoom is < 1 or > 3)
        {
            return BadRequest(new { error = "Thumbnail framing must use a focal point between 0 and 100 and zoom between 1 and 3." });
        }

        var gallery = await _db.Galleries
            .FirstOrDefaultAsync(candidate => candidate.Id == galleryId && candidate.OwnerId == userId);

        if (gallery is null)
            return NotFound(new { error = "Gallery not found or does not belong to the current user." });

        gallery.ThumbnailFocalPointX = req.FocalPointX;
        gallery.ThumbnailFocalPointY = req.FocalPointY;
        gallery.ThumbnailZoom = req.Zoom;
        await _db.SaveChangesAsync();

        return Ok(await CreateThumbnailResponseAsync(gallery));
    }

    // [x] Add photo to database (auth required)
    [Authorize]
    [HttpPost("{galleryId:guid}/photos")]
    public async Task<IActionResult> AddPhotoToGallery(
        Guid galleryId,
        AddPhotoToGalleryRequest req)
    {
        // 1. Find current user from JWT
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // 2. Check if the gallery exists and belongs to the current user
        var gallery = await _db.Galleries
            .AsNoTracking()
            .Where(g => g.Id == galleryId && g.OwnerId == userId)
            .Select(g => new { g.Purpose })
            .FirstOrDefaultAsync();

        if (gallery is null)
            return NotFound(new { error = "Gallery not found." });


        // 3. Object key validation and checks
        // Make sure objectKey is provided and belongs to this user's gallery
        var objectKey = req.ObjectKey.Trim();

        if (string.IsNullOrWhiteSpace(objectKey))
            return BadRequest(new { error = "Object key is required." });

        var expectedPrefix =
            $"users/{userId}/galleries/{galleryId}/photos/";

        if (!objectKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                error = "Object key does not belong to this gallery."
            });
        }

        // Check if the object key exists in SQL database (to prevent duplicates)
        var photoAlreadyExists = await _db.Photos
            .AsNoTracking()
            .AnyAsync(p => p.ObjectKey == objectKey);

        if (photoAlreadyExists)
            return Conflict(new { error = "This photo has already been added." });

        // Verify that the file was actually uploaded to S3
        try
        {
            var metadata = await _s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = _s3Options.BucketName,
                    Key = objectKey
                },
                HttpContext.RequestAborted
            );

            if (metadata.ContentLength == 0)
            {
                return BadRequest(new
                {
                    error = "The uploaded image is empty."
                });
            }
        }
        catch (AmazonS3Exception ex)
            when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return BadRequest(new
            {
                error = "The image has not been uploaded to S3."
            });
        }

        int nextSortOrder;

        if (gallery.Purpose != GalleryPurpose.Moodboard &&
            (req.InsertAtStart || req.InsertAfterPhotoId.HasValue))
        {
            var photos = await _db.Photos
                .Where(p => p.GalleryId == galleryId)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.CreatedAtUtc)
                .ThenBy(p => p.Id)
                .ToListAsync();

            var insertIndex = 0;

            if (req.InsertAfterPhotoId.HasValue)
            {
                insertIndex = photos.FindIndex(p => p.Id == req.InsertAfterPhotoId.Value);
                if (insertIndex < 0)
                    return BadRequest(new { error = "The insertion photo does not belong to this gallery." });

                insertIndex++;
            }

            for (var index = insertIndex; index < photos.Count; index++)
                photos[index].SortOrder = index + 1;

            nextSortOrder = insertIndex;
        }
        else
        {
            nextSortOrder = await _db.Photos
                .Where(p => p.GalleryId == galleryId)
                .MaxAsync(p => (int?)p.SortOrder) ?? -1;

            nextSortOrder++;
        }

        // 5. Create a new photo entity and save it to the database
        var photo = new Photo
        {
            Id = req.PhotoId == Guid.Empty ? Guid.NewGuid() : req.PhotoId,
            GalleryId = galleryId,
            ObjectKey = objectKey,
            Caption = string.IsNullOrWhiteSpace(req.Caption) ? null : req.Caption.Trim(),
            SortOrder = nextSortOrder,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Photos.Add(photo);
        await _db.SaveChangesAsync();

        var imageUrl = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _s3Options.BucketName,
            Key = photo.ObjectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(30)
        });

        var res = new PhotoResponse
        {
            Id = photo.Id,
            GalleryId = photo.GalleryId,
            ObjectKey = photo.ObjectKey,
            ImageUrl = imageUrl,
            Caption = photo.Caption,
            SortOrder = photo.SortOrder,
            BoardX = photo.BoardX,
            BoardY = photo.BoardY,
            BoardWidth = photo.BoardWidth,
            CaptionHeight = photo.CaptionHeight,
            BoardZIndex = photo.BoardZIndex,
            CreatedAtUtc = photo.CreatedAtUtc
        };

        return Created($"/api/galleries/{galleryId}/photos/{photo.Id}", res);
    }

    [Authorize]
    [HttpPost("{galleryId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid galleryId)
    {
        // 1. Find current user from JWT
        var userIdText =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // 2. Find the gallery and ensure it belongs to the current user
        var gallery = await _db.Galleries
            .FirstOrDefaultAsync(g =>
                g.Id == galleryId &&
                g.OwnerId == userId);

        if (gallery is null)
        {
            return NotFound(new
            {
                error = "Gallery not found."
            });
        }

        // 3. Check if the gallery has any photos
        var hasPhotos = await _db.Photos
            .AnyAsync(p => p.GalleryId == galleryId);

        if (!hasPhotos)
        {
            return BadRequest(new
            {
                error = "Add at least one photo before publishing."
            });
        }

        // 4. Publish the gallery
        gallery.IsPublished = true;
        gallery.PublishedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            gallery.Id,
            gallery.IsPublished,
            gallery.PublishedAtUtc
        });
    }

    [Authorize]
    [HttpPost("{galleryId:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid galleryId)
    {
        var userIdText =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        var gallery = await _db.Galleries
            .FirstOrDefaultAsync(g =>
                g.Id == galleryId &&
                g.OwnerId == userId);

        if (gallery is null)
            return NotFound(new { error = "Gallery not found." });

        gallery.IsPublished = false;
        gallery.PublishedAtUtc = null;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            gallery.Id,
            gallery.IsPublished
        });
    }

 // [x] Link a gallery to a collaboration post (auth required)
    [Authorize]
    [HttpPatch("{galleryId:guid}/collab-post")]
    public async Task<IActionResult> UpdateCollaboration(Guid galleryId, UpdateGalleryCollabPostRequest req)
    {
        // 1. Find current user from JWT
        var userIdText =
        User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // 2. Find the gallery and ensure it belongs to the current user
        var gallery = await _db.Galleries
        .FirstOrDefaultAsync(g =>
            g.Id == galleryId &&
            g.OwnerId == userId);

        if (gallery is null)
            return NotFound(new { error = "Gallery not found." });

        // 3. If CollabPostId is provided, validate the user's connection to it
        if (req.CollabPostId.HasValue)
        {
            var error = await ValidateCollabPostLinkAsync(
                userId,
                req.CollabPostId.Value,
                gallery.Purpose,
                gallery.Id);

            if (error is not null)
                return error;
        }

        // A null value unlinks the gallery.
        gallery.CollabPostId = req.CollabPostId;
        var saveError = await SaveGalleryChangesAsync();
        if (saveError is not null)
            return saveError;

        return Ok(new
        {
            gallery.Id,
            gallery.CollabPostId
        });
    }

}
