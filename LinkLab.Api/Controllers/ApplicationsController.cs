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
            return Unauthorized();

        // 2) load application + its post (need owner check)
        var app = await _db.Applications
            .Include(a => a.Post)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (app is null) return NotFound();

        // 3) owner-only authorization
        if (app.Post.UserId != userId) return Forbid(); // 403

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