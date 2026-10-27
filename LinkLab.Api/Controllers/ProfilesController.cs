using LinkLab.Api.Data;
using LinkLab.Api.Dto;
using LinkLab.Api.Options;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAmazonS3 _s3;
    private readonly S3Options _s3Options;

    public ProfilesController(AppDbContext db, IAmazonS3 s3, IOptions<S3Options> s3Options)
    {
        _db = db;
        _s3 = s3;
        _s3Options = s3Options.Value;
    }

    private string? CreateAvatarReadUrl(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) return null;

        return _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _s3Options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddHours(12)
        });
    }

    private string? CreateCoverReadUrl(string? objectKey) => CreateAvatarReadUrl(objectKey);

    // Public profiles expose only a creator's display name and identifier.
    // Their visible work is loaded from the existing public post/gallery APIs.
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.AvatarObjectKey,
                user.AvatarFocalPointX,
                user.AvatarFocalPointY,
                user.AvatarZoom,
                user.CoverObjectKey,
                user.CoverFocalPointX,
                user.CoverFocalPointY,
                user.CoverZoom
            })
            .FirstOrDefaultAsync();

        return profile is null
            ? NotFound(new { error = "Profile not found." })
            : Ok(new PublicProfileResponse(
                profile.Id,
                profile.DisplayName,
                CreateAvatarReadUrl(profile.AvatarObjectKey),
                profile.AvatarFocalPointX,
                profile.AvatarFocalPointY,
                profile.AvatarZoom,
                CreateCoverReadUrl(profile.CoverObjectKey),
                profile.CoverFocalPointX,
                profile.CoverFocalPointY,
                profile.CoverZoom));
    }
}
