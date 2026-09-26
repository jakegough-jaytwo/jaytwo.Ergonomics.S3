using System;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// One object from a list operation. <see cref="Key"/> is relative to
/// <see cref="S3Client.KeyPrefix"/>.
/// </summary>
public sealed class S3ObjectListItem
{
    public S3ObjectListItem(
        string key,
        long size,
        DateTimeOffset? lastModified = null,
        string? eTag = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Size = size;
        LastModified = lastModified;
        ETag = eTag;
    }

    public string Key { get; }

    public long Size { get; }

    public DateTimeOffset? LastModified { get; }

    public string? ETag { get; }

    internal static S3ObjectListItem FromListed(S3Object item, string relativeKey)
    {
        Guard.NotNull(item, nameof(item));

        return new S3ObjectListItem(
            relativeKey,
            size: item.Size ?? 0,
            lastModified: S3Timestamps.ToDateTimeOffsetOrNull(item.LastModified),
            eTag: item.ETag);
    }
}
