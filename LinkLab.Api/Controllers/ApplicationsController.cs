using LinkLab.Api.Data;
using LinkLab.Api.Domain;
using LinkLab.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/applications")]
public class ApplicationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ApplicationsController(AppDbContext db)
    {
        _db = db;
    }

    [Authorize]
    [HttpGet("mine")]
    [ProducesResponseType(typeof(PagedResponse<MyApplicationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
        => await ListSubmittedAsync(page, pageSize, acceptedOnly: false);

    [Authorize]
    [HttpGet("mine/accepted")]
    [ProducesResponseType(typeof(PagedResponse<MyApplicationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListAccepted(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
        => await ListSubmittedAsync(page, pageSize, acceptedOnly: true);

    private async Task<IActionResult> ListSubmittedAsync(int page, int pageSize, bool acceptedOnly)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        if (page < 1)
            return BadRequest(new { error = "Page must be at least 1." });
        if (pageSize < 1 || pageSize > 50)
            return BadRequest(new { error = "Page size must be between 1 and 50." });

        var query = _db.Applications.AsNoTracking()
            .Where(a => a.ApplicantUserId == userId);
        if (acceptedOnly)
            query = query.Where(a => a.Status == ApplicationStatus.Accepted);
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new MyApplicationResponse(
                a.Id, a.PostId, a.Post.Title, a.Message,
                a.Status.ToString(), a.CreatedAtUtc, a.DecidedAtUtc))
            .ToListAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PagedResponse<MyApplicationResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasPreviousPage = page > 1,
            HasNextPage = page < totalPages
        });
    }

    [Authorize]
    [HttpGet("received")]
    [ProducesResponseType(typeof(PagedResponse<ReceivedApplicationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListReceived(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        if (page < 1)
            return BadRequest(new { error = "Page must be at least 1." });
        if (pageSize < 1 || pageSize > 50)
            return BadRequest(new { error = "Page size must be between 1 and 50." });

        var query = _db.Applications.AsNoTracking()
            .Where(a => a.Post.UserId == userId);
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ReceivedApplicationResponse(
                a.Id, a.PostId, a.Post.Title, a.ApplicantUserId,
                a.ApplicantUser.DisplayName, a.Message, a.Status.ToString(),
                a.CreatedAtUtc, a.DecidedAtUtc))
            .ToListAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PagedResponse<ReceivedApplicationResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasPreviousPage = page > 1,
            HasNextPage = page < totalPages
        });
    }

    [Authorize]
    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id)
        => await Decide(id, ApplicationStatus.Accepted);

    [Authorize]
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id)
        => await Decide(id, ApplicationStatus.Rejected);

    private async Task<IActionResult> Decide(Guid applicationId, ApplicationStatus newStatus)
    {
        // 1) current user id
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "Invalid token user." });

        // 2) load application + its post (need owner check)
        var app = await _db.Applications
            .Include(a => a.Post)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (app is null)
            return NotFound(new { error = "Application not found." });

        // 3) owner-only authorization
        if (app.Post.UserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "You are not allowed to decide this application."
            });

        // 4) only pending can be decided
        if (app.Status != ApplicationStatus.Pending)
            return Conflict(new { error = "Application already decided." });

        // 5) apply decision
        app.Status = newStatus;
        app.DecidedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // 6) response
        var res = new ApplicationResponse
        {
            Id = app.Id,
            PostId = app.PostId,
            ApplicantUserId = app.ApplicantUserId,
            Message = app.Message,
            Status = app.Status.ToString(),
            CreatedAtUtc = app.CreatedAtUtc,
            DecidedAtUtc = app.DecidedAtUtc
        };

        return Ok(res);
    }
}
