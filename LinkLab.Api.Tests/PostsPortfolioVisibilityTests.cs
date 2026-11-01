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

public sealed class PostsPortfolioVisibilityTests
{
    [Theory]
    [InlineData("post-owner")]
    [InlineData("gallery-owner")]
    [InlineData("visitor")]
    [InlineData("anonymous")]
    public async Task PostPreviews_IncludeDraftsOnlyForTheirGalleryOwner(string viewer)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var postOwner = new User { Email = "post@test.com", PasswordHash = "unused" };
        var galleryOwner = new User { Email = "gallery@test.com", PasswordHash = "unused" };
        var post = new CollabPost { User = postOwner, Title = "Collaboration" };
        var published = new Gallery
        {
            Owner = galleryOwner, CollabPost = post, Purpose = GalleryPurpose.Portfolio,
            Title = "Published portfolio", IsPublished = true, PublishedAtUtc = DateTime.UtcNow
        };
        var draft = new Gallery
        {
            Owner = galleryOwner, CollabPost = post, Purpose = GalleryPurpose.Portfolio,
            Title = "Draft portfolio"
        };
        db.AddRange(published, draft);
        await db.SaveChangesAsync();
        var viewerId = viewer switch
        {
            "post-owner" => postOwner.Id,
            "gallery-owner" => galleryOwner.Id,
            _ => Guid.NewGuid()
        };
        var principal = viewer == "anonymous" ? new ClaimsPrincipal() : new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, viewerId.ToString()) }, "Test"));
        var controller = new PostsController(db, null!, Microsoft.Extensions.Options.Options.Create(new S3Options()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
        };

        async Task AssertOnlyPublished()
        {
            var detail = Assert.IsType<CollabPostResponse>(Assert.IsType<OkObjectResult>(await controller.GetById(post.Id)).Value);
            Assert.Contains(detail.PortfolioGalleries, gallery => gallery.Id == published.Id && gallery.IsPublished);
            Assert.Equal(viewer == "gallery-owner", detail.PortfolioGalleries.Any(gallery => gallery.Id == draft.Id && !gallery.IsPublished));
            Assert.Equal(viewer == "gallery-owner" ? 2 : 1, detail.PortfolioGalleries.Count);
            var feed = Assert.IsType<PagedResponse<CollabPostResponse>>(Assert.IsType<OkObjectResult>(await controller.List()).Value);
            var previews = Assert.Single(feed.Items).PortfolioGalleries;
            Assert.Equal(viewer == "gallery-owner" ? 2 : 1, previews.Count);
            Assert.Equal(viewer == "gallery-owner", previews.Any(gallery => gallery.Id == draft.Id && !gallery.IsPublished));
        }

        await AssertOnlyPublished();
        var moodboard = new Gallery
        {
            Owner = galleryOwner, CollabPost = post, Purpose = GalleryPurpose.Moodboard,
            Title = "Draft moodboard"
        };
        db.Add(moodboard);
        await db.SaveChangesAsync();
        var withMoodboard = Assert.IsType<CollabPostResponse>(Assert.IsType<OkObjectResult>(await controller.GetById(post.Id)).Value);
        Assert.Equal(viewer == "gallery-owner" ? false : (bool?)null, withMoodboard.MoodboardIsPublished);
        var moodboardResult = await controller.GetMoodboard(post.Id);
        if (viewer == "gallery-owner") Assert.IsType<OkObjectResult>(moodboardResult);
        else Assert.IsType<NotFoundObjectResult>(moodboardResult);
        moodboard.IsPublished = true;
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await controller.GetMoodboard(post.Id));
        var publishedMoodboard = Assert.IsType<CollabPostResponse>(Assert.IsType<OkObjectResult>(await controller.GetById(post.Id)).Value);
        Assert.Equal(true, publishedMoodboard.MoodboardIsPublished);
        published.IsPublished = false;
        published.PublishedAtUtc = null;
        await db.SaveChangesAsync();
        var afterUnpublish = Assert.IsType<CollabPostResponse>(Assert.IsType<OkObjectResult>(await controller.GetById(post.Id)).Value);
        Assert.Equal(viewer == "gallery-owner" ? 2 : 0, afterUnpublish.PortfolioGalleries.Count);
        Assert.All(afterUnpublish.PortfolioGalleries, gallery => Assert.False(gallery.IsPublished));
        var feedAfterUnpublish = Assert.IsType<PagedResponse<CollabPostResponse>>(Assert.IsType<OkObjectResult>(await controller.List()).Value);
        Assert.Equal(viewer == "gallery-owner" ? 2 : 0, Assert.Single(feedAfterUnpublish.Items).PortfolioGalleries.Count);
    }
}
