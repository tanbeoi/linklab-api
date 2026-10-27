using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using BCrypt.Net;
using LinkLab.Api.Data;
using LinkLab.Api.Domain;
using LinkLab.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using LinkLab.Api.Options;
using Microsoft.Extensions.Options;

namespace LinkLab.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IAmazonS3 _s3;
    private readonly S3Options _s3Options;

    public AuthController(
        AppDbContext db,
        IConfiguration config,
        IAmazonS3 s3,
        IOptions<S3Options> s3Options)
    {
        _db = db;
        _config = config;
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

    private AuthUserResponse CreateUserResponse(User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        CreateAvatarReadUrl(user.AvatarObjectKey),
        user.AvatarFocalPointX,
        user.AvatarFocalPointY,
        user.AvatarZoom);

    private Guid? GetCurrentUserId()
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdText, out var userId) ? userId : null;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Email and password are required." });

        var exists = await _db.Users.AnyAsync(u => u.Email == email);
        if (exists) return Conflict(new { error = "Email already registered." });

        var user = new User
        {
            Email = email,
            DisplayName = req.DisplayName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        //Auto-login after register:
        var token = CreateJwt(user);
        SetDevAuthCookie(token);
        return Ok(new
        {
            user = CreateUserResponse(user),
            token
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null) return Unauthorized(new { error = "Invalid credentials." });

        var ok = BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
        if (!ok) return Unauthorized(new { error = "Invalid credentials." });

        var token = CreateJwt(user);
        SetDevAuthCookie(token);

        return Ok(new
        {
            user = CreateUserResponse(user),
            token
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { error = "Invalid token user." });

        var user = await _db.Users.FindAsync([userId.Value], HttpContext.RequestAborted);
        return user is null
            ? Unauthorized(new { error = "User not found." })
            : Ok(CreateUserResponse(user));
    }

    [Authorize]
    [HttpPost("avatar/upload-url")]
    public async Task<IActionResult> CreateAvatarUploadUrl(CreatePhotoUploadUrlRequest req)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { error = "Invalid token user." });

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

        var objectKey = $"users/{userId.Value}/avatar/{Guid.NewGuid()}{extension}";
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(10);
        var uploadUrl = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _s3Options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAtUtc,
            ContentType = contentType
        });

        return Ok(new AvatarUploadUrlResponse(
            objectKey,
            uploadUrl,
            CreateAvatarReadUrl(objectKey)!,
            expiresAtUtc));
    }

    [Authorize]
    [HttpPatch("avatar")]
    public async Task<IActionResult> UpdateAvatar(UpdateAvatarRequest req)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue) return Unauthorized(new { error = "Invalid token user." });

        if (!double.IsFinite(req.FocalPointX) || !double.IsFinite(req.FocalPointY) || !double.IsFinite(req.Zoom) ||
            req.FocalPointX is < 0 or > 100 || req.FocalPointY is < 0 or > 100 || req.Zoom is < 1 or > 3)
        {
            return BadRequest(new { error = "Avatar framing must use a focal point between 0 and 100 and zoom between 1 and 3." });
        }

        var user = await _db.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId.Value);
        if (user is null) return Unauthorized(new { error = "User not found." });

        var suppliedObjectKey = (req.ObjectKey ?? string.Empty).Trim();
        var avatarObjectKey = string.IsNullOrEmpty(suppliedObjectKey) ? user.AvatarObjectKey : suppliedObjectKey;
        if (string.IsNullOrWhiteSpace(avatarObjectKey))
            return BadRequest(new { error = "Upload an image before saving an avatar." });

        if (!string.IsNullOrEmpty(suppliedObjectKey))
        {
            var expectedPrefix = $"users/{userId.Value}/avatar/";
            if (!suppliedObjectKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
                return BadRequest(new { error = "The avatar image does not belong to the current user." });

            try
            {
                var metadata = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _s3Options.BucketName,
                    Key = suppliedObjectKey
                }, HttpContext.RequestAborted);

                if (metadata.ContentLength == 0)
                    return BadRequest(new { error = "The uploaded avatar is empty." });
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return BadRequest(new { error = "The avatar has not been uploaded to S3." });
            }
        }

        var previousObjectKey = user.AvatarObjectKey;
        user.AvatarObjectKey = avatarObjectKey;
        user.AvatarFocalPointX = req.FocalPointX;
        user.AvatarFocalPointY = req.FocalPointY;
        user.AvatarZoom = req.Zoom;
        await _db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(previousObjectKey) &&
            !string.Equals(previousObjectKey, avatarObjectKey, StringComparison.Ordinal))
        {
            try
            {
                await _s3.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _s3Options.BucketName,
                    Key = previousObjectKey
                }, HttpContext.RequestAborted);
            }
            catch (AmazonS3Exception)
            {
                // The account now points at its new avatar, so failed cleanup
                // must not make an otherwise successful update appear to fail.
            }
        }

        return Ok(CreateUserResponse(user));
    }

    private string CreateJwt(User user)
    {
        var issuer = _config["Jwt:Issuer"]!;
        var audience = _config["Jwt:Audience"]!;
        var key = _config["Jwt:Key"]!;

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("displayName", user.DisplayName)
        };

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private void SetDevAuthCookie(string token)
{
    if (HttpContext.RequestServices
        .GetRequiredService<IHostEnvironment>()
        .IsDevelopment())
    {
        Response.Cookies.Append("access_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,               // set true only when HTTPS
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(12)
        });
    }
}
}
