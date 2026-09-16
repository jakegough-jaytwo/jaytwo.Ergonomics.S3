using System;
using Microsoft.Extensions.Configuration;

namespace jaytwo.Ergonomics.S3.Tests;

public class MinioServer
{
    private readonly S3ClientOptions _baseOptions;

    public MinioServer(IConfiguration configuration)
        : this(
            useMinioServer: configuration.GetValue<bool>("UseMinioServer"),
            options: configuration.GetSection("Minio").Get<S3ClientOptions>() ?? new S3ClientOptions())
    {
    }

    public MinioServer(bool useMinioServer, S3ClientOptions options)
    {
        UseMinioServer = useMinioServer;
        _baseOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool UseMinioServer { get; }

    /// <summary>
    /// Builds a client pointed at the configured MinIO endpoint with a unique key prefix
    /// so parallel/repeat runs do not collide in the shared test bucket.
    /// Prefer <see cref="CreateScope"/> in tests so objects are deleted afterward.
    /// </summary>
    public S3Client CreateClient(string? keyPrefix = null)
    {
        if (!UseMinioServer)
        {
            throw new InvalidOperationException("MinIO server connections are disabled.");
        }

        var options = new S3ClientOptions
        {
            ServiceUrl = _baseOptions.ServiceUrl,
            BucketName = _baseOptions.BucketName,
            AccessKeyId = _baseOptions.AccessKeyId,
            SecretAccessKey = _baseOptions.SecretAccessKey,
            AuthenticationRegion = _baseOptions.AuthenticationRegion,
            ForcePathStyle = _baseOptions.ForcePathStyle,
            KeyPrefix = keyPrefix ?? $"run-{Guid.NewGuid():N}/",
        };

        return new S3Client(options);
    }

    /// <summary>
    /// Same as <see cref="CreateClient"/>, but deletes all objects under the client's
    /// key prefix when the scope is disposed.
    /// </summary>
    public MinioTestScope CreateScope(string? keyPrefix = null)
        => new MinioTestScope(CreateClient(keyPrefix));
}
