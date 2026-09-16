using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

public sealed partial class S3Client
{
    /// <summary>
    /// Page size used by <see cref="ListObjectsAsync(string?, int?, string?, string?, CancellationToken)"/>
    /// when the caller does not pick one. Matches the S3 server-side maximum for a single page.
    /// </summary>
    public const int DefaultMaxKeys = 1000;

    public Task<PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        return PutObjectCore(key, request => request.InputStream = inputStream, extraConfig: null, cancellationToken);
    }

    public Task<PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        return PutObjectCore(
            key,
            request =>
            {
                request.InputStream = inputStream;
                request.Headers.ContentLength = contentLength;
            },
            extraConfig: null,
            cancellationToken);
    }

    public Task<PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        long? contentLength,
        byte[] md5,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(md5, nameof(md5));
        return PutObjectCore(
            key,
            request =>
            {
                request.InputStream = inputStream;
                if (contentLength is long length)
                {
                    request.Headers.ContentLength = length;
                }

                request.MD5Digest = Convert.ToBase64String(md5);
            },
            extraConfig: null,
            cancellationToken);
    }

    public Task<PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectCore(key, request => request.InputStream = inputStream, configureRequest, cancellationToken);
    }

    public async Task<PutObjectResponse> PutObjectAsync(
        string key,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(bytes, nameof(bytes));

        using var stream = new MemoryStream(bytes, writable: false);
        return await PutObjectAsync(key, stream, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PutObjectResponse> PutObjectAsync(
        string key,
        byte[] bytes,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(bytes, nameof(bytes));
        Guard.NotNull(configureRequest, nameof(configureRequest));

        using var stream = new MemoryStream(bytes, writable: false);
        return await PutObjectAsync(key, stream, configureRequest, cancellationToken).ConfigureAwait(false);
    }

    public Task<PutObjectResponse> PutObjectAsync(
        string key,
        string content,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(content, nameof(content));
        return PutObjectCore(key, request => request.ContentBody = content, extraConfig: null, cancellationToken);
    }

    public Task<PutObjectResponse> PutObjectAsync(
        string key,
        string content,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(content, nameof(content));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectCore(key, request => request.ContentBody = content, configureRequest, cancellationToken);
    }

    public Task<GetObjectResponse> GetObjectAsync(string key, CancellationToken cancellationToken = default)
        => GetObjectCore(key, extraConfig: null, cancellationToken);

    public Task<GetObjectResponse> GetObjectAsync(
        string key,
        Action<GetObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return GetObjectCore(key, configureRequest, cancellationToken);
    }

    public Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string key, CancellationToken cancellationToken = default)
        => GetObjectMetadataCore(key, extraConfig: null, cancellationToken);

    public Task<GetObjectMetadataResponse> GetObjectMetadataAsync(
        string key,
        Action<GetObjectMetadataRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return GetObjectMetadataCore(key, configureRequest, cancellationToken);
    }

    public Task<bool> ObjectExistsAsync(string key, CancellationToken cancellationToken = default)
        => AmazonS3.ObjectExistsAsync(BucketName, GetFullKey(key), cancellationToken);

    public Task<DeleteObjectResponse> DeleteObjectAsync(string key, CancellationToken cancellationToken = default)
        => DeleteObjectCore(key, extraConfig: null, cancellationToken);

    public Task<DeleteObjectResponse> DeleteObjectAsync(
        string key,
        Action<DeleteObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return DeleteObjectCore(key, configureRequest, cancellationToken);
    }

    /// <summary>
    /// Lists a single page of objects, at most <see cref="DefaultMaxKeys"/> of them unless
    /// <paramref name="maxKeys"/> says otherwise. Pass <see cref="S3ObjectPage.NextContinuationToken"/>
    /// back in to get the next page.
    /// </summary>
    /// <param name="prefix">Prefix relative to <see cref="KeyPrefix"/>.</param>
    /// <param name="maxKeys">Page size; null means <see cref="DefaultMaxKeys"/>. S3 caps this at 1000.</param>
    /// <param name="continuationToken">Token from a previous page.</param>
    /// <param name="startAfter">Key relative to <see cref="KeyPrefix"/> to resume after (exclusive); ignored when <paramref name="continuationToken"/> is set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<S3ObjectPage> ListObjectsAsync(
        string? prefix = null,
        int? maxKeys = null,
        string? continuationToken = null,
        string? startAfter = null,
        CancellationToken cancellationToken = default)
        => ListObjectsCore(prefix, maxKeys, continuationToken, startAfter, extraConfig: null, cancellationToken);

    /// <summary>
    /// Lists a single page of objects with full control over the SDK request. <c>MaxKeys</c> is
    /// pre-set to <see cref="DefaultMaxKeys"/>; <c>StartAfter</c> and any other key-valued property
    /// you set must be a full key (see <see cref="GetFullKey"/>).
    /// </summary>
    public Task<S3ObjectPage> ListObjectsAsync(
        string? prefix,
        Action<ListObjectsV2Request> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return ListObjectsCore(prefix, maxKeys: null, continuationToken: null, startAfter: null, configureRequest, cancellationToken);
    }

    /// <summary>
    /// Streams every object under <paramref name="prefix"/>, following continuation tokens until
    /// the listing is exhausted. There is no upper bound on how many objects (or requests) this
    /// produces, so either stop enumerating early or use <see cref="ListObjectsAsync(string?, int?, string?, string?, CancellationToken)"/>
    /// when the caller is paging.
    /// </summary>
    /// <param name="prefix">Prefix relative to <see cref="KeyPrefix"/>.</param>
    /// <param name="startAfter">Key relative to <see cref="KeyPrefix"/> to resume after (exclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<S3Object> ListAllObjectsAsync(
        string? prefix = null,
        string? startAfter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? token = null;
        do
        {
            var page = await ListObjectsCore(
                prefix,
                maxKeys: null,
                token,
                token is null ? startAfter : null,
                extraConfig: null,
                cancellationToken).ConfigureAwait(false);

            foreach (var item in page.Objects)
            {
                yield return item;
            }

            token = page.NextContinuationToken;
        }
        while (token is not null);
    }

    private async Task<S3ObjectPage> ListObjectsCore(
        string? prefix,
        int? maxKeys,
        string? continuationToken,
        string? startAfter,
        Action<ListObjectsV2Request>? extraConfig,
        CancellationToken cancellationToken)
    {
        if (maxKeys < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxKeys), maxKeys, "maxKeys must be at least 1.");
        }

        var request = new ListObjectsV2Request
        {
            BucketName = BucketName,
            Prefix = ToObjectPrefix(prefix),
            MaxKeys = maxKeys ?? DefaultMaxKeys,
            ContinuationToken = continuationToken,
            StartAfter = string.IsNullOrEmpty(startAfter) ? null : GetFullKey(startAfter!),
        };
        extraConfig?.Invoke(request);

        var response = await AmazonS3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

        var objects = response.S3Objects;
        if (objects is not null)
        {
            foreach (var item in objects)
            {
                item.Key = GetRelativeKey(item.Key);
            }
        }

        var nextToken = response.IsTruncated == true && !string.IsNullOrEmpty(response.NextContinuationToken)
            ? response.NextContinuationToken
            : null;

        return new S3ObjectPage(objects, nextToken);
    }

    private Task<PutObjectResponse> PutObjectCore(
        string key,
        Action<PutObjectRequest> bindBody,
        Action<PutObjectRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = GetFullKey(key),
            AutoCloseStream = false,
        };
        bindBody(request);
        extraConfig?.Invoke(request);
        return AmazonS3.PutObjectAsync(request, cancellationToken);
    }

    private Task<GetObjectResponse> GetObjectCore(
        string key,
        Action<GetObjectRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        var request = new GetObjectRequest
        {
            BucketName = BucketName,
            Key = GetFullKey(key),
        };
        extraConfig?.Invoke(request);
        return AmazonS3.GetObjectAsync(request, cancellationToken);
    }

    private Task<GetObjectMetadataResponse> GetObjectMetadataCore(
        string key,
        Action<GetObjectMetadataRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        var request = new GetObjectMetadataRequest
        {
            BucketName = BucketName,
            Key = GetFullKey(key),
        };
        extraConfig?.Invoke(request);
        return AmazonS3.GetObjectMetadataAsync(request, cancellationToken);
    }

    private Task<DeleteObjectResponse> DeleteObjectCore(
        string key,
        Action<DeleteObjectRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = GetFullKey(key),
        };
        extraConfig?.Invoke(request);
        return AmazonS3.DeleteObjectAsync(request, cancellationToken);
    }

    private string? ToObjectPrefix(string? relativePrefix)
    {
        var combined = S3Key.Combine(Options.KeyPrefix, relativePrefix);
        return combined.Length == 0 ? null : combined;
    }
}
