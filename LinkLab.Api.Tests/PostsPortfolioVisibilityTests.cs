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
    public async Task PostPreviews_OnlyIncludePublishedPortfolios_ForEveryViewer(string viewer)
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
            Assert.Equal(published.Id, Assert.Single(detail.PortfolioGalleries).Id);
            var feed = Assert.IsType<PagedResponse<CollabPostResponse>>(Assert.IsType<OkObjectResult>(await controller.List()).Value);
            Assert.Equal(published.Id, Assert.Single(Assert.Single(feed.Items).PortfolioGalleries).Id);
        }

        await AssertOnlyPublished();
        published.IsPublished = false;
        published.PublishedAtUtc = null;
        await db.SaveChangesAsync();
        var afterUnpublish = Assert.IsType<CollabPostResponse>(Assert.IsType<OkObjectResult>(await controller.GetById(post.Id)).Value);
        Assert.Empty(afterUnpublish.PortfolioGalleries);
        var feedAfterUnpublish = Assert.IsType<PagedResponse<CollabPostResponse>>(Assert.IsType<OkObjectResult>(await controller.List()).Value);
        Assert.Empty(Assert.Single(feedAfterUnpublish.Items).PortfolioGalleries);
    }
}
