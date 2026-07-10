namespace LinkLab.Api.Options;

public class S3Options
{
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
}