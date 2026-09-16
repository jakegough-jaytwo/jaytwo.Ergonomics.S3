using System;
using System.Diagnostics;
using Amazon.S3;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// Bucket-scoped facade over <see cref="IAmazonS3"/>: one bucket, optional key prefix,
/// and the common object/bucket operations. The underlying client stays public.
/// </summary>
[DebuggerDisplay("Bucket = {BucketName}, Prefix = {KeyPrefix}")]
public sealed partial class S3Client : IDisposable
{
    private readonly bool _ownsClient;

    public S3Client(S3ClientOptions options)
        : this(options, AmazonS3ClientFactory.Create(options), ownsClient: true)
    {
    }

    public S3Client(S3ClientOptions options, IAmazonS3 amazonS3)
        : this(options, amazonS3, ownsClient: false)
    {
    }

    internal S3Client(S3ClientOptions options, IAmazonS3 amazonS3, bool ownsClient)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        AmazonS3 = amazonS3 ?? throw new ArgumentNullException(nameof(amazonS3));
        _ownsClient = ownsClient;

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
