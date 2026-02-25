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

        if (desc.Length < 20 || desc.Length > 2000)
            return BadRequest(new { error = "Description must be 20-2000 characters." });

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
}