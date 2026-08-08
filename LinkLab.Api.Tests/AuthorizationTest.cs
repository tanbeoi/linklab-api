using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LinkLab.Api.Dto;

namespace LinkLab.Api.Tests;


public class AuthorizationTests 
    : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{

    private readonly CustomWebApplicationFactory _factory;   
    private readonly HttpClient _client;
    public AuthorizationTests(
        CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

        public Task InitializeAsync()
    {
        return _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    // Test 1: Test the /api/auth/me endpoint without authentication
    [Fact]
    public async Task Me_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Test 2: Test the /api/auth/me endpoint with valid authentication
    // Fact has no parameters
    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUser()
    {
        // Arrange
        var token = await CreateTestUserAndGetTokenAsync();

        // Act 
        _client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Test 3: Test unrelated user cannot link gallery to collab post
    [Fact]
    public async Task UnrelatedUser_CannotLinkGalleryToCollabPost()
    {
        // Arrange 
        var tokenUser1 = await CreateTestUserAndGetTokenAsync();
        var tokenUser2 = await CreateTestUserAndGetTokenAsync();

        // User 1 creates a collab post
        _client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", tokenUser1);
        var collabPostResponse = await _client.PostAsJsonAsync
        (
            "api/posts", 
            new
            {
                title = "User 1 Collab Post",
                description = "This is a collab post created by User 1.",
                location = "Melbourne",
                isRemote = false
            }
        );

        collabPostResponse.EnsureSuccessStatusCode();
        var collabPost = await collabPostResponse.Content
        .ReadFromJsonAsync<CollabPostResponse>();

        // Check if response body is successfully converted to collabPost object
        Assert.NotNull(collabPost);

        var collabPostId = collabPost.Id;

        // User 2 creates a gallery
        _client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", tokenUser2);

        // Act 
        var galleryResponse = await _client.PostAsJsonAsync
        (
            "api/galleries",
            new
            {
                title = "User 2 Gallery",
                description = "This is a gallery created by User 2.",
                collabPostId = collabPostId
            }
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, galleryResponse.StatusCode);
    }

    // Test 4: check if an anonymous user can see a published gallery 
    [Fact]
    public async Task AnonymousUser_CanSeePublishedGallery()
    {
        // ARRANGE      
        // Create User Token
        var userToken = await CreateTestUserAndGetTokenAsync();

        _client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", userToken);

        // User creates a gallery
        var galleryResponse = await _client.PostAsJsonAsync
        (
            "api/galleries",
            new
            {
                title = "User Gallery",
                description = "Test Gallery.",
                collabPostId = (Guid?)null
            }
        );

        galleryResponse.EnsureSuccessStatusCode();

        // Get the gallery ID from the response
        var uploadedGallery = await galleryResponse.Content
        .ReadFromJsonAsync<GalleryResponse>();

        Assert.NotNull(uploadedGallery);

        var uploadedGalleryId = uploadedGallery.Id;

        // User gets photo upload URL
        var photoUploadUrlResponse = await _client.PostAsJsonAsync
        (
            $"api/galleries/{uploadedGalleryId}/photos/upload-url",
            new
            {
                fileName = "lookback1.png",
                contentType = "image/png"
            }
        );

        photoUploadUrlResponse.EnsureSuccessStatusCode();

        // Get the upload URL, photoID and objectKey from the response
        var imageUpload = await photoUploadUrlResponse.Content
        .ReadFromJsonAsync<CreatePhotoUploadUrlResponse>();

        Assert.NotNull(imageUpload);

        var uploadUrl = imageUpload.UploadUrl;
        var photoId = imageUpload.PhotoId;
        var objectKey = imageUpload.ObjectKey;

        // Find the test image in the test output directory
        var imagePath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "lookback1.png"
        );

        Assert.True(
            File.Exists(imagePath),
            $"Test image was not found at {imagePath}"
        );

        // Read the image file into bytes
        var imageBytes = await File.ReadAllBytesAsync(imagePath);

        using var imageContent = new ByteArrayContent(imageBytes);

        imageContent.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        using var s3Client = new HttpClient();

        var s3Response = await s3Client.PutAsync(
            uploadUrl,
            imageContent
        );

        s3Response.EnsureSuccessStatusCode();

        // Saves the image to PostgreSQL database
        var savePhotoResponse = await _client.PostAsJsonAsync(
            $"api/galleries/{uploadedGalleryId}/photos",
            new
            {
                photoId = photoId,
                objectKey = objectKey,
                caption = "Test Image"
            }
        );

        savePhotoResponse.EnsureSuccessStatusCode();

        // User publishes the gallery
        var publishResponse = await _client.PostAsync(
            $"api/galleries/{uploadedGalleryId}/publish",
            null
        );
    
        publishResponse.EnsureSuccessStatusCode();

        // ACT
        // Anonymous user tries to access the published gallery
        _client.DefaultRequestHeaders.Authorization = null; // Remove auth header
        var anonymousResponse = await _client.GetAsync(
            $"api/galleries/{uploadedGalleryId}/photos"
        );

        // ASSERT
        Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);

    }


    // Test 5: Check if page 2 of post list displays the correct post to test pagination
    [Fact]
    public async Task ListPosts_PageTwo_ReturnsCorrectPostsAndMetadata()
    {
        // ARRANGE
        // Create a test user and get their token
        var token = await CreateTestUserAndGetTokenAsync();

        // Create 5 posts to test pagination
        _client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);

        var createdPosts = new List<CollabPostResponse>();

        for (int i = 1; i <= 5; i++)
        {
            var postResponse = await _client.PostAsJsonAsync
            (
                "api/posts",
                new
                {
                    title = $"Test Post {i}",
                    description = $"This is test post number {i}.",
                    location = "Melbourne",
                    isRemote = false
                }
            );

            postResponse.EnsureSuccessStatusCode();
            var post = await postResponse.Content.ReadFromJsonAsync<CollabPostResponse>();
            Assert.NotNull(post);
            createdPosts.Add(post);
        }

        // ACT
        // Request page 2 with page size of 2
        var listResponse = await _client.GetAsync(
            "api/posts?page=2&pageSize=2"
        );

        // ASSERT
        // http response should be 200 OK
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var result = await listResponse.Content.ReadFromJsonAsync<PagedResponse<CollabPostResponse>>();

        Assert.NotNull(result);

        // Check returned items and pagination metadata
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    // Helper method
    private async Task<string> CreateTestUserAndGetTokenAsync()
    {
        var email = $"test-{Guid.NewGuid():N}@linklab.test";

        var response = await _client.PostAsJsonAsync
        (
            "/api/auth/register",
            new
            {
                email,
                password = "Password123!",
                displayName = "Test User"
            }
        );

        // Ensure it is 200
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();

        return token
        ?? throw new InvalidOperationException
        (
            "Registration did not return a token."
        );
    }

    
    
}