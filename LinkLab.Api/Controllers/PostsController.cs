using System.Security.Claims;
using LinkLab.Api.Data;
using LinkLab.Api.Domain;
using LinkLab.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/posts")]
public class PostsController : ControllerBase
{
    private readonly AppDbContext _db;

    public PostsController(AppDbContext db)
    {
        _db = db;
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
            user.DisplayName
        );

        return CreatedAtAction(nameof(GetById), new { id = post.Id }, res);
    }

    // [x] List posts (public)
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 20)
    {
        if (limit < 1) limit = 1;
        if (limit > 50) limit = 50;

        var posts = await _db.CollabPosts
            .AsNoTracking()
            .Include(p => p.User)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(limit)
            .Select(p => new CollabPostResponse(
                p.Id,
                p.Title,
                p.Description,
                p.Location,
                p.IsRemote,
                p.CreatedAtUtc,
                p.UserId,
                p.User != null ? p.User.DisplayName : ""
            ))
            .ToListAsync();

        return Ok(posts);
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
                p.User != null ? p.User.DisplayName : ""
            ))
            .FirstOrDefaultAsync();

        if (post is null) return NotFound(new { error = "Post not found." });

        return Ok(post);
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
        if (message.Length == 0) return BadRequest("Message is required.");
        if (message.Length > 2000) return BadRequest("Message must be <= 2000 characters.");

        // 3) Ensure post exists
        var post = await _db.CollabPosts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == postId);

        if (post is null) return NotFound();

        // Optional: prevent applying to own post
        if (post.UserId == userId) return BadRequest("You cannot apply to your own post.");

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
            new
            {
                application.Id,
                application.PostId,
                application.ApplicantUserId,
                application.Message,
                Status = application.Status.ToString(),
                application.CreatedAtUtc,
                application.DecidedAtUtc
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