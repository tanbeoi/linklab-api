using System.Security.Claims;
using LinkLab.Api.Data;
using LinkLab.Api.Domain;
using LinkLab.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Amazon.S3;
using Amazon.S3.Model;
using LinkLab.Api.Options;
using Microsoft.Extensions.Options;

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/posts")]
public class PostsController : ControllerBase
{
private readonly AppDbContext _db;
private readonly IAmazonS3 _s3;
private readonly S3Options _s3Options;

public PostsController(
    AppDbContext db,
    IAmazonS3 s3,
    IOptions<S3Options> s3Options)
{
    _db = db;
    _s3 = s3;
    _s3Options = s3Options.Value;
}

    private async Task<List<CollabPostResponse>> AddMoodboardPreviewsAsync(
        IReadOnlyList<CollabPostResponse> posts)
    {
        if (posts.Count == 0)
            return new List<CollabPostResponse>();

        var postIds = posts.Select(p => p.Id).ToList();

        // Fetch previews for all returned posts together
        // This avoids N+1 queries and reduces the number of S3 requests.
        // 
        var moodboards = await _db.Galleries
            .AsNoTracking()
            .Where(g =>
                g.CollabPostId.HasValue &&
                postIds.Contains(g.CollabPostId.Value) &&
                g.Purpose == GalleryPurpose.Moodboard &&
                g.IsPublished)
            .Select(g => new
            {
                PostId = g.CollabPostId!.Value,
                PhotoCount = g.Photos.Count,
                ObjectKeys = g.Photos
                    .OrderBy(p => p.SortOrder)
                    .ThenBy(p => p.Id)
                    .Take(3)
                    .Select(p => p.ObjectKey)
                    .ToList()
            })
            .ToListAsync();

        var moodboardsByPostId = moodboards.ToDictionary(m => m.PostId);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(30);

        return posts.Select(post =>
        {
            // If no moodboard exists for this post, return the post as-is.
            if (!moodboardsByPostId.TryGetValue(post.Id, out var moodboard))
                return post;

            // Generate URLs after the database query has completed.
            var imageUrls = moodboard.ObjectKeys
                .Select(key => _s3.GetPreSignedURL(new GetPreSignedUrlRequest
                {
                    BucketName = _s3Options.BucketName,
                    Key = key,
                    Verb = HttpVerb.GET,
                    Expires = expiresAtUtc
                }))
                .ToList();
            
            // return a new CollabPostResponse with the moodboard data included
            return post with
            {
                MoodboardPreviewImageUrls = imageUrls,
                MoodboardPhotoCount = moodboard.PhotoCount
            };
        }).ToList();
    }

    // [x] Create post (auth required)
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(CreateCollabPostRequest req)
    {
        // Basic validation (v1)
        var title = (req.Title ?? "").Trim();
        var desc = (req.Description ?? "").Trim();
        var location = (req.Location ?? "").Trim();

        if (title.Length < 5 || title.Length > 100)
            return BadRequest(new { error = "Title must be 5-100 characters." });

        if (desc.Length < 5 || desc.Length > 2000)
            return BadRequest(new { error = "Description must be 5-2000 characters." });

        if (location.Length > 100)
            return BadRequest(new { error = "Location must be <= 100 characters." });

        // Get current user from JWT
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // Load user display name (for response)
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Unauthorized(new { error = "User not found." });

        var post = new CollabPost
        {
            UserId = userId,
            Title = title,
            Description = desc,
            Location = location,
            IsRemote = req.IsRemote,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CollabPosts.Add(post);
        await _db.SaveChangesAsync();

        var res = new CollabPostResponse(
            post.Id,
            post.Title,
            post.Description,
            post.Location,
            post.IsRemote,
            post.CreatedAtUtc,
            post.UserId,
            user.DisplayName,
            Array.Empty<string>(),
            0
        );

        return CreatedAtAction(nameof(GetById), new { id = post.Id }, res);
    }

    // [x] List posts (public)
    [HttpGet]
    [ProducesResponseType(
        typeof(PagedResponse<CollabPostResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
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

        // 2. Build the base query
        var query = _db.CollabPosts
            .AsNoTracking();

        // 3. Count all matching posts
        var totalCount = await query.CountAsync();

        // 4. Calculate how many posts to skip
        var skip = (page - 1) * pageSize;

        // 5. Load only the requested page
        var posts = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(p => new CollabPostResponse(
                p.Id,
                p.Title,
                p.Description,
                p.Location,
                p.IsRemote,
                p.CreatedAtUtc,
                p.UserId,
                p.User.DisplayName,
                Array.Empty<string>(), // MoodboardPreviewImageUrls
                0                     // MoodboardPhotoCount
            ))
            .ToListAsync();

        posts = await AddMoodboardPreviewsAsync(posts);

        // 6. Calculate the total number of pages
        // Use double for page size to keep decimal precision, then round up to the nearest whole number using Math.Ceiling.
        var totalPages = (int)Math.Ceiling(
            totalCount / (double)pageSize
        );

        // 7. Return posts and pagination information
        return Ok(new PagedResponse<CollabPostResponse>
        {
            Items = posts,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasPreviousPage = page > 1,
            HasNextPage = page < totalPages
        });
    }

    // [x] View post (public)
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var post = await _db.CollabPosts
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p => p.Id == id)
            .Select(p => new CollabPostResponse(
                p.Id,
                p.Title,
                p.Description,
                p.Location,
                p.IsRemote,
                p.CreatedAtUtc,
                p.UserId,
                p.User != null ? p.User.DisplayName : "",
                Array.Empty<string>(), // MoodboardPreviewImageUrls
                0                     // MoodboardPhotoCount
            ))
            .FirstOrDefaultAsync();

        

        if (post is null) return NotFound(new { error = "Post not found." });

        // since post is a single item, we can create a single-item array and return the first item of the result.
        var enrichedPosts = await AddMoodboardPreviewsAsync(new[] { post });

        return Ok(enrichedPosts[0]);
    }

    // [x] Apply to post (auth required)
    [Authorize]
    [HttpPost("{postId:guid}/apply")]
    public async Task<IActionResult> ApplyToPost(Guid postId, [FromBody] ApplyToPostRequest req)
    {
        // 1) Get userId from JWT
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        // 2) Basic validation
        var message = (req.Message ?? string.Empty).Trim();
        if (message.Length == 0) return BadRequest(new { error = "Message is required." });
        if (message.Length > 2000) return BadRequest(new { error = "Message must be <= 2000 characters." });

        // 3) Ensure post exists
        var post = await _db.CollabPosts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == postId);

        if (post is null) return NotFound();

        // Optional: prevent applying to own post
        if (post.UserId == userId) return BadRequest(new { error = "You cannot apply to your own post." });

        // 4) Prevent duplicates (1 application per user per post)
        var alreadyApplied = await _db.Applications
            .AsNoTracking()
            .AnyAsync(a => a.PostId == postId && a.ApplicantUserId == userId);

        if (alreadyApplied) return Conflict("You already applied to this post.");

        // 5) Create application
        var application = new Application
        {
            PostId = postId,
            ApplicantUserId = userId,
            Message = message,
            Status = ApplicationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            DecidedAtUtc = null
        };

        _db.Applications.Add(application);

        // 6) Save
        await _db.SaveChangesAsync();

        // 7) Return 201 Created
        // Don't have a GET /api/applications/{id} yet, just return the created object/ids
        return Created(
            $"/api/applications/{application.Id}",
            new ApplicationResponse
            {
                Id = application.Id,
                PostId = application.PostId,
                ApplicantUserId = application.ApplicantUserId,
                Message = application.Message,
                Status = application.Status.ToString(),
                CreatedAtUtc = application.CreatedAtUtc,
                DecidedAtUtc = application.DecidedAtUtc
            }
        );
    }

    [Authorize]
    [HttpGet("{postId:guid}/applications")]
    public async Task<IActionResult> GetApplicationsForPost(Guid postId)
    {
        // 1) current user id from JWT
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        // 2) load post (need owner check)
        var post = await _db.CollabPosts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == postId);

        if (post is null) return NotFound();

        // 3) owner-only authorization
        if (post.UserId != userId) return Forbid(); // 403

        // 4) query applications + applicant user info
        var apps = await _db.Applications
            .AsNoTracking()
            .Where(a => a.PostId == postId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new ApplicationListItemResponse
            {
                Id = a.Id,
                PostId = a.PostId,
                ApplicantUserId = a.ApplicantUserId,
                ApplicantEmail = a.ApplicantUser.Email,
                ApplicantDisplayName = a.ApplicantUser.DisplayName,
                Message = a.Message,
                Status = a.Status.ToString(),
                CreatedAtUtc = a.CreatedAtUtc,
                DecidedAtUtc = a.DecidedAtUtc
            })
            .ToListAsync();

        return Ok(apps);
    }

    
}
