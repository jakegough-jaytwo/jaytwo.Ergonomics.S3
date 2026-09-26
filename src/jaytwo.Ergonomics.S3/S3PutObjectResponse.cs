using System;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// Result of <see cref="S3Client.PutObjectAsync(string, System.IO.Stream, System.Threading.CancellationToken)"/>
/// or <see cref="S3Client.PutObjectMultipartAsync(string, System.IO.Stream, long, System.Threading.CancellationToken)"/>.
/// <see cref="Key"/> is relative to <see cref="S3Client.KeyPrefix"/> — bucket and full S3 key are not exposed.
/// </summary>
public sealed class S3PutObjectResponse
{
    public S3PutObjectResponse(
        string key,
        string? eTag = null,
        string? versionId = null,
        long? contentLength = null,
        int? partCount = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        ETag = eTag;
        VersionId = versionId;
        ContentLength = contentLength;
        PartCount = partCount;
    }

    /// <summary>Object key relative to <see cref="S3Client.KeyPrefix"/>.</summary>
    public string Key { get; }

    public string? ETag { get; }

    public string? VersionId { get; }

    /// <summary>
    /// Total bytes written when known (seekable/length-bound single put, or multipart sum).
    /// </summary>
    public long? ContentLength { get; }

    /// <summary>
    /// Number of parts uploaded for multipart; <c>null</c> for a single-request put.
    /// </summary>
    public int? PartCount { get; }

    /// <summary><c>true</c> when this result came from <c>PutObjectMultipartAsync</c>.</summary>
    public bool IsMultipart => PartCount is not null;

    internal static S3PutObjectResponse FromPut(string relativeKey, PutObjectResponse response, long? contentLength)
    {
        Guard.NotNull(response, nameof(response));

        return new S3PutObjectResponse(
            relativeKey,
            eTag: response.ETag,
            versionId: response.VersionId,
            contentLength: contentLength,
            partCount: null);
    }

    internal static S3PutObjectResponse FromMultipart(
        string relativeKey,
        CompleteMultipartUploadResponse response,
        long contentLength,
        int partCount)
    {
        Guard.NotNull(response, nameof(response));

        return new S3PutObjectResponse(
            relativeKey,
            eTag: response.ETag,
            versionId: response.VersionId,
            contentLength: contentLength,
            partCount: partCount);
    }
}
