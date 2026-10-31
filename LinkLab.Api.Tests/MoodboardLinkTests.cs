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
    public async Task AcceptedCollaborator_CanCreateOrLinkMoodboard_ButCannotDuplicateOrModifyOwnersGallery()
    {
        var collaborator = new User { Email = "collaborator@test.com", PasswordHash = "unused" };
        _db.Applications.Add(new Application
        {
            PostId = _post.Id, ApplicantUser = collaborator, Status = ApplicationStatus.Accepted
        });
        await _db.SaveChangesAsync();
        var controller = Controller(collaborator.Id);
        var created = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await controller.Create(Request(GalleryPurpose.Moodboard, _post.Id))).Value);
        Assert.Equal(collaborator.Id, created.OwnerId);
        Assert.IsType<ConflictObjectResult>(await Controller(_owner.Id).Create(
            Request(GalleryPurpose.Moodboard, _post.Id)));
        var standalone = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await controller.Create(Request(GalleryPurpose.Moodboard, null))).Value);
        Assert.IsType<ConflictObjectResult>(await controller.UpdateCollaboration(
            standalone.Id, new() { CollabPostId = _post.Id }));
        Assert.IsType<OkObjectResult>(await controller.UpdateCollaboration(created.Id, new()));
        Assert.IsType<OkObjectResult>(await controller.UpdateCollaboration(
            standalone.Id, new() { CollabPostId = _post.Id }));
        Assert.IsType<OkObjectResult>(await controller.UpdateCollaboration(
            standalone.Id, new() { CollabPostId = _post.Id }));
        Assert.IsType<CreatedResult>(await controller.Create(Request(GalleryPurpose.Portfolio, _post.Id)));
        Assert.IsType<OkObjectResult>(await controller.UpdateCollaboration(standalone.Id, new()));
        var owned = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await Controller(_owner.Id).Create(Request(GalleryPurpose.Moodboard, _post.Id))).Value);
        Assert.IsType<NotFoundObjectResult>(await controller.UpdateCollaboration(owned.Id, new()));
        Assert.IsType<ConflictObjectResult>(await controller.Create(Request(GalleryPurpose.Moodboard, _post.Id)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ApplicationStatus.Pending)]
    [InlineData(ApplicationStatus.Rejected)]
    public async Task NonCollaborator_CannotCreateOrLinkMoodboard(ApplicationStatus? status)
    {
        var user = new User { Email = "other@test.com", PasswordHash = "unused" };
        _db.Users.Add(user);
        if (status.HasValue)
            _db.Applications.Add(new Application
            {
                PostId = _post.Id, ApplicantUser = user, Status = status.Value
            });
        await _db.SaveChangesAsync();
        var controller = Controller(user.Id);

        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Create(
            Request(GalleryPurpose.Moodboard, _post.Id))).StatusCode);
        var standalone = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await controller.Create(Request(GalleryPurpose.Moodboard, null))).Value);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.UpdateCollaboration(
            standalone.Id, new() { CollabPostId = _post.Id })).StatusCode);
        Assert.Null((await _db.Galleries.FindAsync(standalone.Id))!.CollabPostId);
    }

    [Fact]
    public async Task Details_KeepLink_WhenChangingType_AndRejectDuplicateMoodboardConversion()
    {
        var controller = Controller(_owner.Id);
        var gallery = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await controller.Create(Request(GalleryPurpose.Moodboard, _post.Id))).Value);
        Assert.IsType<OkObjectResult>(await controller.Update(gallery.Id, new()
        {
            Title = "Portfolio", Description = "Updated", Purpose = GalleryPurpose.Portfolio
        }));
        Assert.Equal(_post.Id, (await _db.Galleries.FindAsync(gallery.Id))!.CollabPostId);
        Assert.IsType<CreatedResult>(await controller.Create(Request(GalleryPurpose.Moodboard, _post.Id)));
        Assert.IsType<OkObjectResult>(await controller.Update(gallery.Id, new()
        {
            Title = "Still portfolio", Purpose = GalleryPurpose.Portfolio
        }));
        Assert.IsType<ConflictObjectResult>(await controller.Update(gallery.Id, new()
        {
            Title = "Blocked conversion", Purpose = GalleryPurpose.Moodboard
        }));
        Assert.Equal(GalleryPurpose.Portfolio, (await _db.Galleries.FindAsync(gallery.Id))!.Purpose);
        Assert.IsType<OkObjectResult>(await controller.UpdateCollaboration(gallery.Id, new()));
        Assert.IsType<OkObjectResult>(await controller.Update(gallery.Id, new()
        {
            Title = "Unlinked moodboard", Purpose = GalleryPurpose.Moodboard
        }));
        Assert.Null((await _db.Galleries.FindAsync(gallery.Id))!.CollabPostId);
    }

    private const string Embed = "<iframe width=\"560\" height=\"315\" src=\"https://www.youtube.com/embed/M7lc1UVf-VE?si=test\" allowfullscreen></iframe>";

    [Theory]
    [InlineData("https://evil.example/embed/M7lc1UVf-VE")]
    [InlineData("https://youtube.com.evil.example/embed/M7lc1UVf-VE")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://www.youtube.com/embed/invalid")]
    [InlineData("<iframe src=\"https://evil.example/embed/M7lc1UVf-VE\"></iframe>")]
    public void YoutubeEmbed_RejectsInvalidSources(string input)
    {
        Assert.False(YoutubeEmbed.TryParse(input, out _, out _));
    }

    [Fact]
    public void YoutubeEmbed_ExtractsOnlyVideoIdAndDimensions()
    {
        Assert.True(YoutubeEmbed.TryParse(Embed, out var id, out var ratio));
        Assert.Equal("M7lc1UVf-VE", id);
        Assert.Equal(16d / 9, ratio);
        Assert.True(YoutubeEmbed.TryParse("<iframe width='315' height='560' src='https://www.youtube-nocookie.com/embed/M7lc1UVf-VE'></iframe>", out _, out ratio));
        Assert.Equal(315d / 560, ratio);
    }

    [Fact]
    public async Task Videos_PreserveMixedOrder_AndBlockMoodboardConversionUntilRemoved()
    {
        var controller = Controller(_owner.Id);
        var gallery = Assert.IsType<GalleryResponse>(Assert.IsType<CreatedResult>(
            await controller.Create(Request(GalleryPurpose.Portfolio, _post.Id))).Value);
        var image = new Photo { Id = Guid.NewGuid(), GalleryId = gallery.Id, ObjectKey = "image", SortOrder = 3 };
        _db.Photos.Add(image);
        await _db.SaveChangesAsync();
        var video = Assert.IsType<PhotoResponse>(Assert.IsType<OkObjectResult>(await controller.AddYoutubeVideo(
            gallery.Id, new() { EmbedCode = Embed, InsertAfterPhotoId = image.Id })).Value);
        var first = Assert.IsType<PhotoResponse>(Assert.IsType<OkObjectResult>(await controller.AddYoutubeVideo(
            gallery.Id, new() { EmbedCode = Embed, InsertAtStart = true })).Value);
        var middle = Assert.IsType<PhotoResponse>(Assert.IsType<OkObjectResult>(await controller.AddYoutubeVideo(
            gallery.Id, new() { EmbedCode = Embed, InsertAfterPhotoId = image.Id })).Value);
        Assert.Equal(new[] { first.Id, image.Id, middle.Id, video.Id },
            await _db.Photos.Where(p => p.GalleryId == gallery.Id).OrderBy(p => p.SortOrder).Select(p => p.Id).ToArrayAsync());
        Assert.Equal("M7lc1UVf-VE", video.YoutubeVideoId);
        Assert.IsType<BadRequestObjectResult>(await controller.Update(gallery.Id, new()
        { Title = "Moodboard", Purpose = GalleryPurpose.Moodboard }));
        Assert.Equal(GalleryPurpose.Portfolio, (await _db.Galleries.FindAsync(gallery.Id))!.Purpose);
        Assert.IsType<NotFoundObjectResult>(await Controller(Guid.NewGuid()).AddYoutubeVideo(
            gallery.Id, new() { EmbedCode = Embed }));
        Assert.IsType<BadRequestObjectResult>(await controller.AddYoutubeVideo(
            gallery.Id, new() { EmbedCode = Embed, InsertAfterPhotoId = Guid.NewGuid() }));
        foreach (var item in new[] { first, middle, video })
            Assert.IsType<NoContentResult>(await controller.DeletePhoto(gallery.Id, item.Id));
        Assert.IsType<OkObjectResult>(await controller.Update(gallery.Id, new()
        { Title = "Moodboard", Purpose = GalleryPurpose.Moodboard }));
        Assert.IsType<BadRequestObjectResult>(await controller.AddYoutubeVideo(
            gallery.Id, new() { EmbedCode = Embed }));
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
