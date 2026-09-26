using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// Result of get or head. Same shape either way: attributes always present,
/// <see cref="Body"/> is the object stream on get and <c>null</c> on head.
/// <see cref="Key"/> is relative to <see cref="S3Client.KeyPrefix"/>.
/// Dispose releases the body stream (when present).
/// </summary>
public sealed class S3ObjectResponse : IDisposable
{
    private static readonly Dictionary<string, string> EmptyDictionary =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly IDisposable? _lifetime;
    private int _disposed;

    public S3ObjectResponse(
        string key,
        long size,
        DateTimeOffset? lastModified = null,
        string? eTag = null,
        string? contentType = null,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string>? metadata = null,
        Stream? body = null)
        : this(key, size, lastModified, eTag, contentType, headers, metadata, body, lifetime: null)
    {
    }

    private S3ObjectResponse(
        string key,
        long size,
        DateTimeOffset? lastModified,
        string? eTag,
        string? contentType,
        IDictionary<string, string>? headers,
        IDictionary<string, string>? metadata,
        Stream? body,
        IDisposable? lifetime)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Size = size;
        LastModified = lastModified;
        ETag = eTag;
        ContentType = contentType;
        Headers = ToReadOnly(headers);
        Metadata = ToReadOnly(metadata);
        Body = body;
        _lifetime = lifetime;
    }

    public string Key { get; }

    public long Size { get; }

    public DateTimeOffset? LastModified { get; }

    public string? ETag { get; }

    public string? ContentType { get; }

    /// <summary>HTTP response headers from get/head (content-type, cache-control, …).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>User metadata (<c>x-amz-meta-*</c>).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Object body on get; <c>null</c> on head.
    /// </summary>
    public Stream? Body { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // GetObjectResponse.Dispose closes ResponseStream (including our instrumented wrapper).
        if (_lifetime is not null)
        {
            _lifetime.Dispose();
            return;
        }

        Body?.Dispose();
    }

    internal static S3ObjectResponse FromGet(string relativeKey, GetObjectResponse response)
    {
        Guard.NotNull(response, nameof(response));

        return new S3ObjectResponse(
            relativeKey,
            size: response.ContentLength,
            lastModified: S3Timestamps.ToDateTimeOffsetOrNull(response.LastModified),
            eTag: response.ETag,
            contentType: response.Headers?.ContentType,
            headers: response.Headers?.ToDictionary(),
            metadata: response.Metadata?.ToDictionary(),
            body: response.ResponseStream,
            lifetime: response);
    }

    internal static S3ObjectResponse FromHead(string relativeKey, GetObjectMetadataResponse response)
    {
        Guard.NotNull(response, nameof(response));

        return new S3ObjectResponse(
            relativeKey,
            size: response.ContentLength,
            lastModified: S3Timestamps.ToDateTimeOffsetOrNull(response.LastModified),
            eTag: response.ETag,
            contentType: response.Headers?.ContentType,
            headers: response.Headers?.ToDictionary(),
            metadata: response.Metadata?.ToDictionary(),
            body: null,
            lifetime: null);
    }

    private static IReadOnlyDictionary<string, string> ToReadOnly(IDictionary<string, string>? values)
    {
        if (values is null)
        {
            return EmptyDictionary;
        }

        if (values is Dictionary<string, string> dict)
        {
            return dict;
        }

        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }
}
