using System.Security.Claims;
using LinkLab.Api.Controllers;
using LinkLab.Api.Data;
using LinkLab.Api.Domain;
using LinkLab.Api.Dto;
using LinkLab.Api.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LinkLab.Api.Tests;

public sealed class MoodboardLinkTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly User _owner = new() { Email = "owner@test.com", PasswordHash = "unused" };
    private readonly CollabPost _post;

    public MoodboardLinkTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _post = new CollabPost { User = _owner };
        _db.Add(_post);
        _db.SaveChanges();
    }

    private GalleriesController Controller(Guid userId) => new(_db, null!, Microsoft.Extensions.Options.Options.Create(new S3Options()))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test"))
            }
        }
    };

    private CreateGalleryRequest Request(GalleryPurpose purpose, Guid? postId) => new()
    {
        Title = "Test gallery", Purpose = purpose, CollabPostId = postId
    };

    [Fact]
    public async Task Create_RejectsSecondMoodboard_ButAllowsMultiplePortfolios()
    {
        var controller = Controller(_owner.Id);
        Assert.IsType<CreatedResult>(await controller.Create(Request(GalleryPurpose.Moodboard, _post.Id)));
        Assert.IsType<ConflictObjectResult>(await controller.Create(Request(GalleryPurpose.Moodboard, _post.Id)));
        Assert.IsType<CreatedResult>(await controller.Create(Request(GalleryPurpose.Portfolio, _post.Id)));
        Assert.IsType<CreatedResult>(await controller.Create(Request(GalleryPurpose.Portfolio, _post.Id)));
        Assert.Equal(3, await _db.Galleries.CountAsync());
    }

    [Fact]
    public async Task Update_AllowsSameLink_AndReplacementAfterUnlinking()
    {
        var controller = Controller(_owner.Id);
        var first = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await controller.Create(Request(GalleryPurpose.Moodboard, _post.Id))).Value);
        var second = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await controller.Create(Request(GalleryPurpose.Moodboard, null))).Value);
        var link = new UpdateGalleryCollabPostRequest { CollabPostId = _post.Id };

        Assert.IsType<OkObjectResult>(await controller.UpdateCollaboration(first.Id, link));
        Assert.IsType<ConflictObjectResult>(await controller.UpdateCollaboration(second.Id, link));
        Assert.Null((await _db.Galleries.FindAsync(second.Id))!.CollabPostId);
        Assert.IsType<OkObjectResult>(await controller.UpdateCollaboration(first.Id, new()));
        Assert.IsType<OkObjectResult>(await controller.UpdateCollaboration(second.Id, link));
    }

    [Fact]
    public async Task AcceptedCollaborator_CannotCreateOrLinkMoodboard_OrModifyOwnersGallery()
    {
        var collaborator = new User { Email = "collaborator@test.com", PasswordHash = "unused" };
        _db.Applications.Add(new Application
        {
            PostId = _post.Id, ApplicantUser = collaborator, Status = ApplicationStatus.Accepted
        });
        await _db.SaveChangesAsync();
        var controller = Controller(collaborator.Id);
        Assert.Equal(403, Assert.IsType<ObjectResult>(
            await controller.Create(Request(GalleryPurpose.Moodboard, _post.Id))).StatusCode);
        var standalone = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await controller.Create(Request(GalleryPurpose.Moodboard, null))).Value);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.UpdateCollaboration(
            standalone.Id, new() { CollabPostId = _post.Id })).StatusCode);
        Assert.IsType<CreatedResult>(await controller.Create(Request(GalleryPurpose.Portfolio, _post.Id)));
        var owned = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await Controller(_owner.Id).Create(Request(GalleryPurpose.Moodboard, _post.Id))).Value);
        Assert.IsType<NotFoundObjectResult>(await controller.UpdateCollaboration(owned.Id, new()));
    }

    [Fact]
    public async Task Database_RejectsDuplicateMoodboards_WhenValidationIsBypassed()
    {
        _db.Galleries.Add(new Gallery { OwnerId = _owner.Id, CollabPostId = _post.Id, Purpose = GalleryPurpose.Moodboard });
        await _db.SaveChangesAsync();
        _db.Galleries.Add(new Gallery { OwnerId = _owner.Id, CollabPostId = _post.Id, Purpose = GalleryPurpose.Moodboard });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
