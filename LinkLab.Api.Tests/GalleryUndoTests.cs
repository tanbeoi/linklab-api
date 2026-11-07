using System.Security.Claims;
using System.Text.Json;
using Amazon;
using Amazon.S3;
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

public sealed class GalleryUndoTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly AmazonS3Client s3 = new("test-key", "test-secret", RegionEndpoint.APSoutheast2);
    private readonly User owner = new() { Email = "undo@test.com", PasswordHash = "unused" };
    private readonly Gallery gallery;

    public GalleryUndoTests()
    {
        connection.Open();
        db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        gallery = new Gallery { Owner = owner, Title = "Undo gallery", Purpose = GalleryPurpose.Moodboard };
        db.Add(gallery); db.SaveChanges();
    }

    private GalleriesController Controller(Guid? userId = null) => new(db, s3,
        Microsoft.Extensions.Options.Options.Create(new S3Options { BucketName = "test-bucket" }))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, (userId ?? owner.Id).ToString()) }, "Test"))
        }}
    };

    private Photo AddPhoto(int order = 0)
    {
        var photo = new Photo { Id = Guid.NewGuid(), GalleryId = gallery.Id, ObjectKey = $"test/{Guid.NewGuid()}.jpg", SortOrder = order, BoardX = 10, BoardY = 20 };
        db.Add(photo); db.SaveChanges(); return photo;
    }

    [Fact]
    public async Task Delete_HidesPhotoEverywhere_RestoreKeepsIdentityAndThumbnail_AndRedoWorks()
    {
        var photo = AddPhoto();
        gallery.IsPublished = true;
        gallery.ThumbnailPhotoId = photo.Id;
        gallery.ThumbnailFocalPointX = 23; gallery.ThumbnailFocalPointY = 71; gallery.ThumbnailZoom = 1.5;
        await db.SaveChangesAsync();
        var controller = Controller();
        Assert.IsType<NoContentResult>(await controller.DeletePhoto(gallery.Id, photo.Id));
        Assert.Empty(await db.Photos.ToListAsync());
        Assert.NotNull((await db.Photos.IgnoreQueryFilters().SingleAsync()).DeletedAtUtc);
        Assert.Equal(0, await db.Galleries.Where(g => g.Id == gallery.Id).Select(g => g.Photos.Count).SingleAsync());
        Assert.Null(gallery.ThumbnailPhotoId);
        Assert.IsType<NoContentResult>(await controller.DeletePhoto(gallery.Id, photo.Id));
        Assert.IsType<NotFoundObjectResult>(await Controller(Guid.NewGuid()).RestorePhoto(gallery.Id, photo.Id));
        var restored = Assert.IsType<OkObjectResult>(await controller.RestorePhoto(gallery.Id, photo.Id));
        var body = JsonSerializer.SerializeToElement(restored.Value);
        Assert.Equal(photo.Id, body.GetProperty("photo").GetProperty("Id").GetGuid());
        Assert.Equal(photo.ObjectKey, body.GetProperty("photo").GetProperty("ObjectKey").GetString());
        Assert.Contains("X-Amz-Signature", body.GetProperty("photo").GetProperty("ImageUrl").GetString());
        Assert.Equal(photo.Id, gallery.ThumbnailPhotoId);
        Assert.Equal(23, gallery.ThumbnailFocalPointX);
        Assert.Equal(71, gallery.ThumbnailFocalPointY);
        Assert.Equal(1.5, gallery.ThumbnailZoom);
        Assert.Single(await db.Photos.ToListAsync());
        Assert.IsType<NoContentResult>(await controller.DeletePhoto(gallery.Id, photo.Id));
        Assert.Empty(await db.Photos.ToListAsync());
    }

    [Fact]
    public async Task Restore_InsertsBeforeNewItemsWithoutSortConflicts()
    {
        gallery.Purpose = GalleryPurpose.Portfolio;
        var first = AddPhoto(0); var second = AddPhoto(1);
        var controller = Controller();
        await controller.DeletePhoto(gallery.Id, first.Id);
        var replacement = AddPhoto(0);
        Assert.IsType<OkObjectResult>(await controller.RestorePhoto(gallery.Id, first.Id));
        Assert.Equal(new[] { first.Id, replacement.Id, second.Id }, await db.Photos.OrderBy(p => p.SortOrder).Select(p => p.Id).ToArrayAsync());
    }

    [Fact]
    public async Task BatchLayouts_MoveGroupAtomically_AndUndoToUnsorted()
    {
        var first = AddPhoto(); var second = AddPhoto(1);
        var controller = Controller();
        var request = new UpdatePhotoLayoutsRequest { Photos = [
            new() { PhotoId = first.Id, BoardX = 100, BoardY = 200, BoardWidth = 240, CaptionHeight = 58, BoardZIndex = 2 },
            new() { PhotoId = second.Id, BoardX = 150, BoardY = 250, BoardWidth = 300, CaptionHeight = 80, BoardZIndex = 3 }
        ] };
        Assert.IsType<NotFoundObjectResult>(await Controller(Guid.NewGuid()).UpdatePhotoLayouts(gallery.Id, request));
        Assert.Equal(10, first.BoardX);
        request.Photos[1].BoardX = -1;
        Assert.IsType<BadRequestObjectResult>(await controller.UpdatePhotoLayouts(gallery.Id, request));
        Assert.Equal(10, first.BoardX);
        request.Photos[1].BoardX = 150;
        Assert.IsType<OkObjectResult>(await controller.UpdatePhotoLayouts(gallery.Id, request));
        Assert.Equal(100, first.BoardX); Assert.Equal(150, second.BoardX);
        Assert.IsType<OkObjectResult>(await controller.UpdatePhotoLayouts(gallery.Id, new() { Photos = [new() { PhotoId = first.Id }] }));
        Assert.Null(first.BoardX); Assert.Null(first.BoardY); Assert.Null(first.BoardWidth); Assert.Null(first.BoardZIndex);
        await controller.DeletePhoto(gallery.Id, second.Id);
        Assert.IsType<NotFoundObjectResult>(await controller.UpdatePhotoLayouts(gallery.Id, request));
        Assert.Null(first.BoardX);
    }

    [Fact]
    public async Task DeletedVideoCannotBeRestoredIntoMoodboard()
    {
        gallery.Purpose = GalleryPurpose.Portfolio;
        var photo = AddPhoto(); photo.YoutubeVideoId = "M7lc1UVf-VE";
        await db.SaveChangesAsync();
        var controller = Controller();
        await controller.DeletePhoto(gallery.Id, photo.Id);
        gallery.Purpose = GalleryPurpose.Moodboard; await db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await controller.RestorePhoto(gallery.Id, photo.Id));
        Assert.Empty(await db.Photos.ToListAsync());
    }

    public void Dispose() { db.Dispose(); connection.Dispose(); s3.Dispose(); }
}
