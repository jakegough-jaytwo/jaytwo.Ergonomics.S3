using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using jaytwo.Ergonomics.Logging;
using jaytwo.Ergonomics.S3.Logging;
using jaytwo.Ergonomics.S3.Tracing;

namespace jaytwo.Ergonomics.S3;

public partial class S3Client
{
    /// <summary>
    /// Page size used by <see cref="ListObjectsAsync(string?, int?, string?, string?, CancellationToken)"/>
    /// when the caller does not pick one. Matches the S3 server-side maximum for a single page.
    /// </summary>
    public const int DefaultMaxKeys = 1000;

    /// <summary>
    /// S3 minimum size for every multipart part except the last.
    /// </summary>
    public const long MinimumMultipartPartLength = 5L * 1024 * 1024;

    /// <summary>
    /// Default <c>partLength</c> for <see cref="PutObjectMultipartAsync(string, Stream, long, CancellationToken)"/>
    /// (8 MiB). Two in-memory buffers of this size are held per in-flight multipart put so the next
    /// part can be filled while the previous <c>UploadPart</c> is in flight.
    /// </summary>
    public const long DefaultMultipartPartLength = 8L * 1024 * 1024;

    /// <summary>
    /// S3 maximum number of parts in a multipart upload.
    /// </summary>
    public const int MaxMultipartParts = 10000;

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        return PutObjectCore(key, request => request.InputStream = inputStream, extraConfig: null, cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(contentType, nameof(contentType));
        return PutObjectAsync(key, inputStream, request => request.ContentType = contentType, cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        string contentType,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(contentType, nameof(contentType));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectAsync(
            key,
            inputStream,
            request =>
            {
                request.ContentType = contentType;
                configureRequest(request);
            },
            cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        long? contentLength,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        return PutObjectAsync(key, inputStream, contentLength, static _ => { }, cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        long? contentLength,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(contentType, nameof(contentType));
        return PutObjectAsync(key, inputStream, contentLength, request => request.ContentType = contentType, cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        long? contentLength,
        string contentType,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(contentType, nameof(contentType));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectAsync(
            key,
            inputStream,
            contentLength,
            request =>
            {
                request.ContentType = contentType;
                configureRequest(request);
            },
            cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        long? contentLength,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectCore(
            key,
            request =>
            {
                request.InputStream = inputStream;
                if (contentLength is long length)
                {
                    request.Headers.ContentLength = length;
                }
            },
            configureRequest,
            cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        Stream inputStream,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectCore(key, request => request.InputStream = inputStream, configureRequest, cancellationToken);
    }

    public async Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(bytes, nameof(bytes));

        using var stream = new MemoryStream(bytes, writable: false);
        return await PutObjectAsync(key, stream, cancellationToken).ConfigureAwait(false);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(bytes, nameof(bytes));
        Guard.NotNull(contentType, nameof(contentType));
        return PutObjectAsync(key, bytes, request => request.ContentType = contentType, cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectAsync(
        string key,
        byte[] bytes,
        string contentType,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(bytes, nameof(bytes));
        Guard.NotNull(contentType, nameof(contentType));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectAsync(
            key,
            bytes,
            request =>
            {
                request.ContentType = contentType;
                configureRequest(request);
            },
            cancellationToken);
    }

    public async Task<S3PutObjectResponse> PutObjectAsync(
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

    public Task<S3PutObjectResponse> PutObjectMultipartAsync(
        string key,
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        return PutObjectMultipartCore(key, inputStream, DefaultMultipartPartLength, extraConfig: null, cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectMultipartAsync(
        string key,
        Stream inputStream,
        long partLength,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        return PutObjectMultipartCore(key, inputStream, partLength, extraConfig: null, cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectMultipartAsync(
        string key,
        Stream inputStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(contentType, nameof(contentType));
        return PutObjectMultipartAsync(
            key,
            inputStream,
            DefaultMultipartPartLength,
            request => request.ContentType = contentType,
            cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectMultipartAsync(
        string key,
        Stream inputStream,
        long partLength,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(contentType, nameof(contentType));
        return PutObjectMultipartAsync(
            key,
            inputStream,
            partLength,
            request => request.ContentType = contentType,
            cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectMultipartAsync(
        string key,
        Stream inputStream,
        string contentType,
        Action<InitiateMultipartUploadRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(contentType, nameof(contentType));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectMultipartAsync(
            key,
            inputStream,
            DefaultMultipartPartLength,
            request =>
            {
                request.ContentType = contentType;
                configureRequest(request);
            },
            cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectMultipartAsync(
        string key,
        Stream inputStream,
        long partLength,
        string contentType,
        Action<InitiateMultipartUploadRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(contentType, nameof(contentType));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectMultipartAsync(
            key,
            inputStream,
            partLength,
            request =>
            {
                request.ContentType = contentType;
                configureRequest(request);
            },
            cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectMultipartAsync(
        string key,
        Stream inputStream,
        Action<InitiateMultipartUploadRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectMultipartCore(key, inputStream, DefaultMultipartPartLength, configureRequest, cancellationToken);
    }

    public Task<S3PutObjectResponse> PutObjectMultipartAsync(
        string key,
        Stream inputStream,
        long partLength,
        Action<InitiateMultipartUploadRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return PutObjectMultipartCore(key, inputStream, partLength, configureRequest, cancellationToken);
    }

    public async Task<S3ObjectResponse> GetObjectAsync(string key, CancellationToken cancellationToken = default)
        => (await GetObjectCore(key, extraConfig: null, returnNullIfNotFound: false, cancellationToken).ConfigureAwait(false))!;

    public async Task<S3ObjectResponse> GetObjectAsync(
        string key,
        Action<GetObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return (await GetObjectCore(key, configureRequest, returnNullIfNotFound: false, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Like <see cref="GetObjectAsync(string, CancellationToken)"/>, but returns <c>null</c> on
    /// not-found instead of throwing. Expected misses log <c>S3:OBJECT_NOT_FOUND</c> at Debug
    /// and count as metric success; throwing Get stays Error on miss.
    /// </summary>
    public Task<S3ObjectResponse?> GetObjectOrNullAsync(string key, CancellationToken cancellationToken = default)
        => GetObjectCore(key, extraConfig: null, returnNullIfNotFound: true, cancellationToken);

    /// <inheritdoc cref="GetObjectOrNullAsync(string, CancellationToken)"/>
    public Task<S3ObjectResponse?> GetObjectOrNullAsync(
        string key,
        Action<GetObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return GetObjectCore(key, configureRequest, returnNullIfNotFound: true, cancellationToken);
    }

    public async Task<byte[]> GetObjectBytesAsync(string key, CancellationToken cancellationToken = default)
    {
        using var response = await GetObjectAsync(key, cancellationToken).ConfigureAwait(false);
        return await ReadBodyBytesAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> GetObjectBytesAsync(
        string key,
        Action<GetObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        using var response = await GetObjectAsync(key, configureRequest, cancellationToken).ConfigureAwait(false);
        return await ReadBodyBytesAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetObjectBytesOrNullAsync(string key, CancellationToken cancellationToken = default)
    {
        using var response = await GetObjectOrNullAsync(key, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        return await ReadBodyBytesAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetObjectBytesOrNullAsync(
        string key,
        Action<GetObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        using var response = await GetObjectOrNullAsync(key, configureRequest, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        return await ReadBodyBytesAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads the object body as a UTF-8 string.
    /// </summary>
    public async Task<string> GetObjectStringAsync(string key, CancellationToken cancellationToken = default)
    {
        var bytes = await GetObjectBytesAsync(key, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <inheritdoc cref="GetObjectStringAsync(string, CancellationToken)"/>
    public async Task<string> GetObjectStringAsync(
        string key,
        Action<GetObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        var bytes = await GetObjectBytesAsync(key, configureRequest, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task<string?> GetObjectStringOrNullAsync(string key, CancellationToken cancellationToken = default)
    {
        var bytes = await GetObjectBytesOrNullAsync(key, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public async Task<string?> GetObjectStringOrNullAsync(
        string key,
        Action<GetObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        var bytes = await GetObjectBytesOrNullAsync(key, configureRequest, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public Task<CopyObjectResponse> CopyObjectAsync(
        string sourceKey,
        string destinationKey,
        CancellationToken cancellationToken = default)
        => CopyObjectCore(sourceKey, destinationKey, extraConfig: null, cancellationToken);

    public Task<CopyObjectResponse> CopyObjectAsync(
        string sourceKey,
        string destinationKey,
        Action<CopyObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return CopyObjectCore(sourceKey, destinationKey, configureRequest, cancellationToken);
    }

    public Task<S3ObjectResponse> HeadObjectAsync(string key, CancellationToken cancellationToken = default)
        => HeadObjectCore(key, extraConfig: null, cancellationToken);

    public Task<S3ObjectResponse> HeadObjectAsync(
        string key,
        Action<GetObjectMetadataRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(configureRequest, nameof(configureRequest));
        return HeadObjectCore(key, configureRequest, cancellationToken);
    }

    public Task<bool> ObjectExistsAsync(string key, CancellationToken cancellationToken = default)
        => ObjectExistsCore(key, cancellationToken);

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
        => ListObjectsCore(prefix, maxKeys, continuationToken, startAfter, extraConfig: null, operationId: null, cancellationToken);

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
        return ListObjectsCore(prefix, maxKeys: null, continuationToken: null, startAfter: null, configureRequest, operationId: null, cancellationToken);
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
    public async IAsyncEnumerable<S3ObjectListItem> ListAllObjectsAsync(
        string? prefix = null,
        string? startAfter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // One operation_id across pages so a full walk correlates as a single multi-event operation.
        var operationId = NewOperationId();

        string? token = null;
        do
        {
            var page = await ListObjectsCore(
                prefix,
                maxKeys: null,
                token,
                token is null ? startAfter : null,
                extraConfig: null,
                operationId,
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
        string? operationId,
        CancellationToken cancellationToken)
    {
        if (maxKeys < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxKeys), maxKeys, "maxKeys must be at least 1.");
        }

        var listPrefix = string.IsNullOrEmpty(prefix) ? null : prefix;
        var keyPrefix = KeyPrefixOrNull();

        var eventLogger = CreateEventLogger(operationId);
        using var activity = StartActivity(ActivityNames.ListObjects, listPrefix, operationId);

        var actionStopwatch = Stopwatch.StartNew();
        try
        {
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

            var objects = MapListedObjects(response.S3Objects);

            var nextToken = response.IsTruncated == true && !string.IsNullOrEmpty(response.NextContinuationToken)
                ? response.NextContinuationToken
                : null;

            var page = new S3ObjectPage(objects, nextToken);
            actionStopwatch.Stop();

            eventLogger?.LogObjectListed(
                BucketName,
                keyPrefix,
                listPrefix,
                actionStopwatch.Elapsed,
                page.Objects.Count,
                page.IsTruncated);
            S3Metrics.RecordSuccess(ActivityNames.ListObjects, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return page;
        }
        catch (Exception ex)
        {
            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.ListObjects,
                BucketName,
                keyPrefix,
                listPrefix,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }
    }

    private async Task<S3PutObjectResponse> PutObjectCore(
        string key,
        Action<PutObjectRequest> bindBody,
        Action<PutObjectRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(key, nameof(key));
        var keyPrefix = KeyPrefixOrNull();

        var eventLogger = CreateEventLogger();
        using var activity = StartActivity(ActivityNames.PutObject, key);

        var actionStopwatch = Stopwatch.StartNew();
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = GetFullKey(key),
                AutoCloseStream = false,
            };
            bindBody(request);
            extraConfig?.Invoke(request);
            var contentLength = TryGetPutContentLength(request);

            var response = await AmazonS3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
            actionStopwatch.Stop();

            eventLogger?.LogObjectPut(BucketName, keyPrefix, key, actionStopwatch.Elapsed, contentLength, response.ETag);
            S3Metrics.RecordSuccess(ActivityNames.PutObject, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return S3PutObjectResponse.FromPut(key, response, contentLength);
        }
        catch (Exception ex)
        {
            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.PutObject,
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }

        static long? TryGetPutContentLength(PutObjectRequest request)
        {
            if (request.Headers.ContentLength > 0)
            {
                return request.Headers.ContentLength;
            }

            if (request.InputStream is { CanSeek: true } stream && stream.Length >= 0)
            {
                return stream.Length;
            }

            return request.ContentBody?.Length;
        }
    }

    private async Task<S3PutObjectResponse> PutObjectMultipartCore(
        string key,
        Stream inputStream,
        long partLength,
        Action<InitiateMultipartUploadRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(key, nameof(key));
        Guard.NotNull(inputStream, nameof(inputStream));

        if (partLength < MinimumMultipartPartLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partLength),
                partLength,
                $"Part length must be at least {MinimumMultipartPartLength} bytes (S3 minimum for non-last parts).");
        }

        if (partLength > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partLength),
                partLength,
                $"Part length must be at most {int.MaxValue} bytes (in-memory part buffer).");
        }

        var keyPrefix = KeyPrefixOrNull();
        var fullKey = GetFullKey(key);
        var eventLogger = CreateEventLogger();
        using var activity = StartActivity(ActivityNames.PutObjectMultipart, key);

        var actionStopwatch = Stopwatch.StartNew();
        string? uploadId = null;
        try
        {
            var initiateRequest = new InitiateMultipartUploadRequest
            {
                BucketName = BucketName,
                Key = fullKey,
            };
            extraConfig?.Invoke(initiateRequest);

            var initiateResponse = await AmazonS3
                .InitiateMultipartUploadAsync(initiateRequest, cancellationToken)
                .ConfigureAwait(false);
            uploadId = initiateResponse.UploadId;

            // Double buffer: fill the next part while the previous UploadPart is in flight so the
            // producer is only ~partLength behind instead of stalling for each round-trip.
            var buffers = new[]
            {
                new byte[(int)partLength],
                new byte[(int)partLength],
            };
            var fillIndex = 0;
            var partETags = new List<PartETag>();
            long totalBytes = 0;
            var nextPartNumber = 1;
            Task<PartETag>? inflightUpload = null;
            var eof = false;

            while (true)
            {
                var filled = await FillPartBufferAsync(
                        inputStream,
                        buffers[fillIndex],
                        () => eof = true,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (filled == 0)
                {
                    if (inflightUpload is not null)
                    {
                        partETags.Add(await inflightUpload.ConfigureAwait(false));
                        inflightUpload = null;
                    }
                    else if (partETags.Count == 0)
                    {
                        if (nextPartNumber > MaxMultipartParts)
                        {
                            throw CreateMultipartPartLimitException();
                        }

                        partETags.Add(
                            await UploadPartBufferAsync(
                                    fullKey,
                                    uploadId,
                                    nextPartNumber,
                                    buffers[fillIndex],
                                    length: 0,
                                    cancellationToken)
                                .ConfigureAwait(false));
                    }

                    break;
                }

                if (inflightUpload is not null)
                {
                    partETags.Add(await inflightUpload.ConfigureAwait(false));
                    inflightUpload = null;
                }

                if (nextPartNumber > MaxMultipartParts)
                {
                    throw CreateMultipartPartLimitException();
                }

                var partNumber = nextPartNumber++;
                totalBytes += filled;
                inflightUpload = UploadPartBufferAsync(
                    fullKey,
                    uploadId,
                    partNumber,
                    buffers[fillIndex],
                    filled,
                    cancellationToken);
                fillIndex ^= 1;

                if (eof)
                {
                    partETags.Add(await inflightUpload.ConfigureAwait(false));
                    inflightUpload = null;
                    break;
                }
            }

            var completeResponse = await AmazonS3
                .CompleteMultipartUploadAsync(
                    new CompleteMultipartUploadRequest
                    {
                        BucketName = BucketName,
                        Key = fullKey,
                        UploadId = uploadId,
                        PartETags = partETags,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            actionStopwatch.Stop();
            uploadId = null;

            eventLogger?.LogObjectMultipartPut(
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed,
                totalBytes,
                partETags.Count,
                completeResponse.ETag);
            S3Metrics.RecordSuccess(ActivityNames.PutObjectMultipart, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return S3PutObjectResponse.FromMultipart(key, completeResponse, totalBytes, partETags.Count);
        }
        catch (Exception ex)
        {
            if (uploadId is not null)
            {
                try
                {
                    await AmazonS3
                        .AbortMultipartUploadAsync(
                            new AbortMultipartUploadRequest
                            {
                                BucketName = BucketName,
                                Key = fullKey,
                                UploadId = uploadId,
                            },
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort abort; surface the original failure.
                }
            }

            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.PutObjectMultipart,
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }

        static InvalidOperationException CreateMultipartPartLimitException()
            => new(
                $"Multipart upload exceeded S3's limit of {MaxMultipartParts} parts. Increase partLength or split the object.");

        static async Task<int> FillPartBufferAsync(
            Stream inputStream,
            byte[] buffer,
            Action onEof,
            CancellationToken cancellationToken)
        {
            var filled = 0;
            while (filled < buffer.Length)
            {
                var read = await inputStream
                    .ReadAsync(buffer, filled, buffer.Length - filled, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    onEof();
                    break;
                }

                filled += read;
            }

            return filled;
        }

        async Task<PartETag> UploadPartBufferAsync(
            string objectKey,
            string multipartUploadId,
            int partNumber,
            byte[] buffer,
            int length,
            CancellationToken uploadCancellationToken)
        {
            using var partStream = new MemoryStream(buffer, 0, length, writable: false);
            var uploadResponse = await AmazonS3
                .UploadPartAsync(
                    new UploadPartRequest
                    {
                        BucketName = BucketName,
                        Key = objectKey,
                        UploadId = multipartUploadId,
                        PartNumber = partNumber,
                        InputStream = partStream,
                        PartSize = length,
                    },
                    uploadCancellationToken)
                .ConfigureAwait(false);

            return new PartETag(partNumber, uploadResponse.ETag);
        }
    }

    private async Task<S3ObjectResponse?> GetObjectCore(
        string key,
        Action<GetObjectRequest>? extraConfig,
        bool returnNullIfNotFound,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(key, nameof(key));
        var keyPrefix = KeyPrefixOrNull();

        // Headers + stream use are two events; operation_id joins them.
        var operationId = NewOperationId();
        var eventLogger = CreateEventLogger(operationId);
        var activity = StartActivity(ActivityNames.GetObject, key, operationId);

        var actionStopwatch = Stopwatch.StartNew();
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = BucketName,
                Key = GetFullKey(key),
            };
            extraConfig?.Invoke(request);

            var response = await AmazonS3.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
            actionStopwatch.Stop();

            eventLogger?.LogObjectGet(BucketName, keyPrefix, key, actionStopwatch.Elapsed, response.ContentLength);
            S3Metrics.RecordSuccess(ActivityNames.GetObject, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);

            // End headers/TTFB before starting use — two spans, two meanings.
            activity?.Dispose();
            activity = null;

            InstrumentGetObjectResponse(response, operationId, eventLogger, keyPrefix, key);
            return S3ObjectResponse.FromGet(key, response);
        }
        catch (AmazonS3Exception ex) when (returnNullIfNotFound && ex.IsNotFound())
        {
            actionStopwatch.Stop();
            // Single-event expected miss: no operation_id (that tag is for multi-event ops).
            CreateEventLogger()?.LogObjectNotFound(
                ActivityNames.GetObject,
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed);
            S3Metrics.RecordSuccess(ActivityNames.GetObject, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return null;
        }
        catch (Exception ex)
        {
            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.GetObject,
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private async Task<CopyObjectResponse> CopyObjectCore(
        string sourceKey,
        string destinationKey,
        Action<CopyObjectRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(sourceKey, nameof(sourceKey));
        Guard.NotNull(destinationKey, nameof(destinationKey));
        var keyPrefix = KeyPrefixOrNull();

        var eventLogger = CreateEventLogger();
        using var activity = StartActivity(ActivityNames.CopyObject, destinationKey);
        activity?.SetTag("aws.s3.copy_source", sourceKey);

        var actionStopwatch = Stopwatch.StartNew();
        try
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = BucketName,
                SourceKey = GetFullKey(sourceKey),
                DestinationBucket = BucketName,
                DestinationKey = GetFullKey(destinationKey),
            };
            extraConfig?.Invoke(request);

            var response = await AmazonS3.CopyObjectAsync(request, cancellationToken).ConfigureAwait(false);
            actionStopwatch.Stop();

            eventLogger?.LogObjectCopied(
                BucketName,
                keyPrefix,
                sourceKey,
                destinationKey,
                actionStopwatch.Elapsed,
                response.ETag);
            S3Metrics.RecordSuccess(ActivityNames.CopyObject, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.CopyObject,
                BucketName,
                keyPrefix,
                destinationKey,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }
    }

    private async Task<byte[]> ReadBodyBytesAsync(
        S3ObjectResponse response,
        CancellationToken cancellationToken)
    {
        if (response.Body is null)
        {
            return Array.Empty<byte>();
        }

        using var memory = new MemoryStream();
        await response.Body.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private void InstrumentGetObjectResponse(
        GetObjectResponse response,
        string operationId,
        S3ObjectEventLogger? eventLogger,
        string? keyPrefix,
        string key)
    {
        if (response.ResponseStream is null)
        {
            return;
        }

        var useActivity = StartActivity(ActivityNames.GetObjectUse, key, operationId);
        var useStopwatch = Stopwatch.StartNew();
        var inner = response.ResponseStream;

        response.ResponseStream = new InstrumentedResponseStream(inner, (bytesRead, fault) =>
        {
            useStopwatch.Stop();
            var elapsed = useStopwatch.Elapsed;

            useActivity?.SetTag("s3.body.size", bytesRead);

            if (fault is null)
            {
                eventLogger?.LogObjectStreamClosed(BucketName, keyPrefix, key, elapsed, bytesRead);
                S3Metrics.RecordSuccess(ActivityNames.GetObjectUse, elapsed);
                useActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                AddExceptionInfo(useActivity, fault);
                useActivity?.SetStatus(ActivityStatusCode.Error, fault.Message);

                void WithBytesRead(LogBuilder builder)
                    => builder.WithFields(("bytes_read", bytesRead));

                // No caller CancellationToken at dispose time; treat OCE as cancel.
                if (fault is OperationCanceledException)
                {
                    eventLogger?.LogCancelled(
                        ActivityNames.GetObjectUse,
                        BucketName,
                        keyPrefix,
                        key,
                        elapsed,
                        fault,
                        WithBytesRead);
                    S3Metrics.RecordFailure(
                        ActivityNames.GetObjectUse,
                        elapsed,
                        S3Metrics.CancelledErrorType);
                }
                else
                {
                    eventLogger?.LogOperationFailed(
                        ActivityNames.GetObjectUse,
                        BucketName,
                        keyPrefix,
                        key,
                        elapsed,
                        fault,
                        WithBytesRead);
                    S3Metrics.RecordFailure(
                        ActivityNames.GetObjectUse,
                        elapsed,
                        fault is AmazonS3Exception s3Ex && s3Ex.IsNotFound()
                            ? S3Metrics.NotFoundErrorType
                            : fault.GetType().FullName);
                }
            }

            useActivity?.Dispose();
        });
    }

    private async Task<S3ObjectResponse> HeadObjectCore(
        string key,
        Action<GetObjectMetadataRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(key, nameof(key));
        var keyPrefix = KeyPrefixOrNull();

        var eventLogger = CreateEventLogger();
        using var activity = StartActivity(ActivityNames.HeadObject, key);

        var actionStopwatch = Stopwatch.StartNew();
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = BucketName,
                Key = GetFullKey(key),
            };
            extraConfig?.Invoke(request);

            var response = await AmazonS3.GetObjectMetadataAsync(request, cancellationToken).ConfigureAwait(false);
            actionStopwatch.Stop();

            var result = S3ObjectResponse.FromHead(key, response);

            eventLogger?.LogObjectHead(
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed,
                result.Size,
                result.ETag);
            S3Metrics.RecordSuccess(ActivityNames.HeadObject, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.HeadObject,
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }
    }

    private IReadOnlyList<S3ObjectListItem> MapListedObjects(IList<S3Object>? objects)
    {
        if (objects is null || objects.Count == 0)
        {
            return Array.Empty<S3ObjectListItem>();
        }

        var mapped = new S3ObjectListItem[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            var item = objects[i];
            mapped[i] = S3ObjectListItem.FromListed(item, GetRelativeKey(item.Key));
        }

        return mapped;
    }

    private async Task<DeleteObjectResponse> DeleteObjectCore(
        string key,
        Action<DeleteObjectRequest>? extraConfig,
        CancellationToken cancellationToken)
    {
        Guard.NotNull(key, nameof(key));
        var keyPrefix = KeyPrefixOrNull();

        var eventLogger = CreateEventLogger();
        using var activity = StartActivity(ActivityNames.DeleteObject, key);

        var actionStopwatch = Stopwatch.StartNew();
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = BucketName,
                Key = GetFullKey(key),
            };
            extraConfig?.Invoke(request);

            var response = await AmazonS3.DeleteObjectAsync(request, cancellationToken).ConfigureAwait(false);
            actionStopwatch.Stop();

            eventLogger?.LogObjectDeleted(BucketName, keyPrefix, key, actionStopwatch.Elapsed);
            S3Metrics.RecordSuccess(ActivityNames.DeleteObject, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.DeleteObject,
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }
    }

    private async Task<bool> ObjectExistsCore(string key, CancellationToken cancellationToken)
    {
        Guard.NotNull(key, nameof(key));
        var keyPrefix = KeyPrefixOrNull();

        var eventLogger = CreateEventLogger();
        using var activity = StartActivity(ActivityNames.ObjectExists, key);

        var actionStopwatch = Stopwatch.StartNew();
        try
        {
            bool exists;
            try
            {
                await AmazonS3.HeadObjectAsync(
                    request =>
                    {
                        request.BucketName = BucketName;
                        request.Key = GetFullKey(key);
                    },
                    cancellationToken).ConfigureAwait(false);
                exists = true;
            }
            catch (AmazonS3Exception ex) when (ex.IsNotFound())
            {
                exists = false;
            }

            actionStopwatch.Stop();

            eventLogger?.LogObjectExists(BucketName, keyPrefix, key, actionStopwatch.Elapsed, exists);
            S3Metrics.RecordSuccess(ActivityNames.ObjectExists, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return exists;
        }
        catch (Exception ex)
        {
            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.ObjectExists,
                BucketName,
                keyPrefix,
                key,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }
    }

    private string? ToObjectPrefix(string? relativePrefix)
    {
        var combined = S3Key.Combine(Options.KeyPrefix, relativePrefix);
        return combined.Length == 0 ? null : combined;
    }
}
