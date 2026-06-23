namespace LinkLab.Api.Domain; 

public class Gallery
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set;}

    // null! tells the compiler that this null value will be set later, so it doesn't throw a warning
    public User Owner { get; set; } = null!;
    public Guid? CollabPostId { get; set; }
    public CollabPost? CollabPost { get; set; }
    public List<Photo> Photos { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

}