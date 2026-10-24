using LinkLab.Api.Data;
using LinkLab.Api.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProfilesController(AppDbContext db)
    {
        _db = db;
    }

    // Public profiles expose only a creator's display name and identifier.
    // Their visible work is loaded from the existing public post/gallery APIs.
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new PublicProfileResponse(user.Id, user.DisplayName))
            .FirstOrDefaultAsync();

        return profile is null ? NotFound(new { error = "Profile not found." }) : Ok(profile);
    }
}
