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

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/galleries")]
public class GalleriesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAmazonS3 _s3;
    private readonly S3Options _s3Options;

    public GalleriesController(AppDbContext db, IAmazonS3 s3, IOptions<S3Options> s3Options)
    {
        _db = db;
        _s3 = s3;
        _s3Options = s3Options.Value;
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

        // If CollabPostId is provided, verify the user's connection to it
        if (req.CollabPostId.HasValue)
        {
            var collabPostId = req.CollabPostId.Value;

            // 1. Find the collab post
            var post = await _db.CollabPosts
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == collabPostId);

            if (post is null)
            {
                return BadRequest(new
                {
                    error = "Collab post does not exist."
                });
            }

            // 2. Check whether the current user created the post
            var isPostOwner = post.UserId == userId;

            // 3. Check whether the current user was accepted onto the post
            var isAcceptedCollaborator = await _db.Applications
                .AsNoTracking()
                .AnyAsync(a =>
                    a.PostId == collabPostId &&
                    a.ApplicantUserId == userId &&
                    a.Status == ApplicationStatus.Accepted);

            // 4. User must satisfy at least one condition
            if (!isPostOwner && !isAcceptedCollaborator)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        error = "You were not involved in this collaboration."
                    });
            }
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
            CollabPostId = req.CollabPostId,
            SortOrder = nextSortOrder + 1,
            CreatedAtUtc = DateTime.UtcNow
        };

        // 2. Save gallery to database
        _db.Galleries.Add(gallery);
        await _db.SaveChangesAsync();

        // 3. Build response
        var res = new GalleryResponse
        {
            Id = gallery.Id,
            Title = gallery.Title,
            Description = gallery.Description,
            OwnerId = gallery.OwnerId,
            IsPublished = gallery.IsPublished,
            CollabPostId = gallery.CollabPostId,
            SortOrder = gallery.SortOrder,
            CreatedAtUtc = gallery.CreatedAtUtc,
            PublishedAtUtc = gallery.PublishedAtUtc,
            PhotoCount = 0
        };

        // Return 201 Created,
        // tell the client where the new gallery lives,
        // and include the created gallery data in the response body.
        return Created($"/api/galleries/{gallery.Id}", res);
    }

    [Authorize]
    [HttpGet]
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
                OwnerId = g.OwnerId,
                IsPublished = g.IsPublished,
                CollabPostId = g.CollabPostId,
                SortOrder = g.SortOrder,
                CreatedAtUtc = g.CreatedAtUtc,
                PublishedAtUtc = g.PublishedAtUtc,
                PhotoCount = g.Photos.Count
            })
            .ToListAsync();

        return Ok(galleries);
    }

    [Authorize]
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
            CreatedAtUtc = p.CreatedAtUtc
        });

        return Ok(res);
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

    // [x] Add photo to gallery (auth required)
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
        var galleryExists = await _db.Galleries
            .AsNoTracking()
            .AnyAsync(g => g.Id == galleryId && g.OwnerId == userId);

        if (!galleryExists)
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

        // Find the next sort order for the photo in the gallery
        var nextSortOrder = await _db.Photos
            .Where(p => p.GalleryId == galleryId)
            .MaxAsync(p => (int?)p.SortOrder) ?? -1;

        // 5. Create a new photo entity and save it to the database
        var photo = new Photo
        {
            Id = req.PhotoId == Guid.Empty ? Guid.NewGuid() : req.PhotoId,
            GalleryId = galleryId,
            ObjectKey = objectKey,
            Caption = string.IsNullOrWhiteSpace(req.Caption) ? null : req.Caption.Trim(),
            SortOrder = nextSortOrder + 1,
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
            return Unauthorized();

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
            return Unauthorized();

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
}
