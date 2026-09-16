namespace jaytwo.Ergonomics.S3;

/// <summary>
/// Settings for <see cref="AmazonS3ClientFactory"/> and bucket-scoped <see cref="S3Client"/>.
/// </summary>
public class S3ClientOptions
{
    /// <summary>
    /// Custom service endpoint (MinIO, LocalStack, or another S3-compatible store).
    /// When set, the factory defaults to path-style addressing and SigV4.
    /// Leave null to use the regional AWS endpoint.
    /// </summary>
    public string? ServiceUrl { get; set; }

    public string? BucketName { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    /// <summary>
    /// Applied to every object key and list prefix on <see cref="S3Client"/>.
    /// A trailing slash is added if missing. Null or empty means no prefix.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Signing / endpoint region. Defaults to <c>us-east-1</c> (MinIO-friendly).
    /// </summary>
    public string AuthenticationRegion { get; set; } = "us-east-1";

    /// <summary>
    /// When null, path-style is on if <see cref="ServiceUrl"/> is set, off otherwise.
    /// </summary>
    public bool? ForcePathStyle { get; set; }
}
