using System;
using System.Diagnostics;
using Amazon.S3;
using Microsoft.Extensions.Logging;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// Bucket-scoped client over <see cref="IAmazonS3"/>: one bucket, optional key prefix,
/// and the common object/bucket operations. Domain stores hold an <see cref="S3Client"/>
/// and name keys. The underlying client stays public.
/// Optional <see cref="ILogger"/> enables structured <c>S3:*</c> events, Activities, and metrics.
/// </summary>
[DebuggerDisplay("Bucket = {BucketName}, Prefix = {KeyPrefix}")]
public sealed partial class S3Client : IDisposable
{
    private readonly bool _ownsClient;
    private readonly ILogger? _logger;

    public S3Client(S3ClientOptions options, ILogger? logger = null)
        : this(options, AmazonS3ClientFactory.Create(options), ownsClient: true, logger)
    {
    }

    public S3Client(S3ClientOptions options, IAmazonS3 amazonS3, ILogger? logger = null)
        : this(options, amazonS3, ownsClient: false, logger)
    {
    }

    internal S3Client(
        S3ClientOptions options,
        IAmazonS3 amazonS3,
        bool ownsClient,
        ILogger? logger = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        AmazonS3 = amazonS3 ?? throw new ArgumentNullException(nameof(amazonS3));
        _ownsClient = ownsClient;
        _logger = logger;

        if (string.IsNullOrEmpty(options.BucketName))
        {
            throw new ArgumentException("BucketName is required.", nameof(options));
        }
    }

    public S3ClientOptions Options { get; }

    public IAmazonS3 AmazonS3 { get; }

    public string BucketName => Options.BucketName!;

    public string KeyPrefix => S3Key.NormalizePrefix(Options.KeyPrefix);

    public string GetFullKey(string key) => S3Key.Combine(Options.KeyPrefix, key);

    public string GetRelativeKey(string fullKey) => S3Key.StripPrefix(fullKey, Options.KeyPrefix);

    public void Dispose()
    {
        if (_ownsClient)
        {
            AmazonS3.Dispose();
        }
    }
}
