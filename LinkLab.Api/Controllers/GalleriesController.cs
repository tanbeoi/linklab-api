using System.Security.Claims;
using LinkLab.Api.Data;
using LinkLab.Api.Domain;
using LinkLab.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/galleries")]
public class GalleriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public GalleriesController(AppDbContext db)
    {
        _db = db;
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

        // If CollabPostId is provided, check if it exists
        if (req.CollabPostId.HasValue)
        {
            var postExists = await _db.CollabPosts
                .AsNoTracking()
                .AnyAsync(p => p.Id == req.CollabPostId.Value);

            if (!postExists)
                return BadRequest(new { error = "CollabPostId does not exist." });
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
            CollabPostId = gallery.CollabPostId,
            SortOrder = gallery.SortOrder,
            CreatedAtUtc = gallery.CreatedAtUtc,
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
                CollabPostId = g.CollabPostId,
                SortOrder = g.SortOrder,
                CreatedAtUtc = g.CreatedAtUtc,
                PhotoCount = g.Photos.Count
            })
            .ToListAsync();

        return Ok(galleries);
    }

    [Authorize]
    [HttpGet("{galleryId:guid}/photos")]
    public async Task<IActionResult> ListPhotos(Guid galleryId)
    {
        // 1. Find current user from JWT
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // 2. Check if the gallery exists and belongs to the current user
        var gallery = await _db.Galleries
            .AsNoTracking()
            .AnyAsync(g => g.Id == galleryId && g.OwnerId == userId);

        if (!gallery)
            return NotFound(new { error = "Gallery not found or does not belong to the current user. " });

        // 3. Get photos for the gallery
        var photos = await _db.Photos
            .AsNoTracking()
            .Where(p => p.GalleryId == galleryId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.CreatedAtUtc)
            .Select(p => new PhotoResponse
            {
                Id = p.Id,
                GalleryId = p.GalleryId,
                ImageUrl = p.ImageUrl,
                Caption = p.Caption,
                SortOrder = p.SortOrder,
                CreatedAtUtc = p.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(photos);
    }
}
