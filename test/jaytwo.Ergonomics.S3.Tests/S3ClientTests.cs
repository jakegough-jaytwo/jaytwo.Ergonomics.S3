using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using jaytwo.Ergonomics.S3;
using Moq;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class S3ClientTests
{
    [Fact]
    public void Constructor_requires_bucket_name()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        var ex = Assert.Throws<ArgumentException>(() =>
            new S3Client(new S3ClientOptions(), amazon.Object));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void Dispose_disposes_owned_client_only()
    {
        var options = new S3ClientOptions { BucketName = "bucket" };

        var owned = new Mock<IAmazonS3>(MockBehavior.Loose);
        using (new S3Client(options, owned.Object, ownsClient: true))
        {
        }

        owned.Verify(x => x.Dispose(), Times.Once);

        var borrowed = new Mock<IAmazonS3>(MockBehavior.Loose);
        using (new S3Client(options, borrowed.Object))
        {
        }

        borrowed.Verify(x => x.Dispose(), Times.Never);
    }

    [Fact]
    public async Task PutObjectAsync_applies_bucket_and_key_prefix()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse { ETag = "\"etag-1\"", VersionId = "v1" });

        using var stream = new MemoryStream(new byte[] { 1 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        var result = await client.PutObjectAsync("notes/a.txt", stream, put => put.ContentType = "text/plain");

        Assert.Equal("notes/a.txt", result.Key);
        Assert.Equal("\"etag-1\"", result.ETag);
        Assert.Equal("v1", result.VersionId);
        Assert.Null(result.PartCount);
        Assert.False(result.IsMultipart);
        Assert.Equal(1, result.ContentLength);

        Assert.NotNull(captured);
        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/notes/a.txt", captured.Key);
        Assert.Equal("text/plain", captured.ContentType);
        Assert.False(captured.AutoCloseStream);
        Assert.Same(stream, captured.InputStream);
    }

    [Fact]
    public async Task PutObjectAsync_bytes_disposes_the_upload_stream()
    {
        Stream? capturedStream = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => capturedStream = request.InputStream)
            .ReturnsAsync(new PutObjectResponse());

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        await client.PutObjectAsync("notes/a.txt", new byte[] { 1, 2, 3 });

        Assert.NotNull(capturedStream);
        Assert.False(capturedStream!.CanRead);
    }

    [Fact]
    public async Task PutObjectAsync_stream_sets_content_length()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync("notes/a.txt", stream, contentLength: 3);

        Assert.NotNull(captured);
        Assert.Same(stream, captured!.InputStream);
        Assert.Equal(3, captured.Headers.ContentLength);
    }

    [Fact]
    public async Task PutObjectAsync_stream_with_null_content_length_skips_length()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync("notes/a.txt", stream, contentLength: null);

        Assert.NotNull(captured);
        Assert.Same(stream, captured!.InputStream);
        Assert.Equal(-1, captured.Headers.ContentLength);
    }

    [Fact]
    public async Task PutObjectAsync_stream_with_content_length_and_configure_sets_both()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync(
            "notes/a.txt",
            stream,
            contentLength: 3,
            request => request.Headers.ContentType = "text/plain");

        Assert.NotNull(captured);
        Assert.Same(stream, captured!.InputStream);
        Assert.Equal(3, captured.Headers.ContentLength);
        Assert.Equal("text/plain", captured.Headers.ContentType);
    }

    [Fact]
    public async Task PutObjectAsync_stream_with_null_content_length_and_configure_skips_length()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync(
            "notes/a.txt",
            stream,
            contentLength: null,
            request => request.Headers.ContentType = "application/octet-stream");

        Assert.NotNull(captured);
        Assert.Same(stream, captured!.InputStream);
        Assert.Equal(-1, captured.Headers.ContentLength);
        Assert.Equal("application/octet-stream", captured.Headers.ContentType);
    }

    [Fact]
    public async Task PutObjectAsync_stream_with_content_type_sets_header()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync("notes/a.txt", stream, "text/plain");

        Assert.NotNull(captured);
        Assert.Equal("text/plain", captured!.ContentType);
        Assert.Same(stream, captured.InputStream);
    }

    [Fact]
    public async Task PutObjectAsync_stream_with_content_length_and_content_type()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync("notes/a.txt", stream, contentLength: 3, contentType: "text/plain");

        Assert.NotNull(captured);
        Assert.Equal(3, captured!.Headers.ContentLength);
        Assert.Equal("text/plain", captured.ContentType);
    }

    [Fact]
    public async Task PutObjectAsync_stream_content_type_then_configure_runs_last()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync(
            "notes/a.txt",
            stream,
            "text/plain",
            request => request.ContentType = "application/json");

        Assert.NotNull(captured);
        Assert.Equal("application/json", captured!.ContentType);
    }

    [Fact]
    public async Task PutObjectAsync_bytes_with_content_type()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        await client.PutObjectAsync("notes/a.txt", new byte[] { 1, 2, 3 }, "image/png");

        Assert.NotNull(captured);
        Assert.Equal("image/png", captured!.ContentType);
    }

    [Fact]
    public async Task PutObjectAsync_bytes_with_content_type_and_configure()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        await client.PutObjectAsync(
            "notes/a.txt",
            Encoding.UTF8.GetBytes("hello"),
            "text/plain",
            put => put.Metadata["k"] = "v");

        Assert.NotNull(captured);
        Assert.Equal("text/plain", captured!.ContentType);
        Assert.Equal("v", captured.Metadata["k"]);
        Assert.NotNull(captured.InputStream);
    }

    [Fact]
    public async Task GetObjectAsync_applies_bucket_and_key_prefix()
    {
        GetObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new GetObjectResponse());

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        using var response = await client.GetObjectAsync("notes/a.txt");

        Assert.NotNull(captured);
        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/notes/a.txt", captured.Key);
        Assert.Equal("notes/a.txt", response.Key);
        Assert.Null(response.Body);
    }

    [Fact]
    public async Task GetObjectBytesAsync_returns_body_bytes()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 3,
                ResponseStream = new MemoryStream(new byte[] { 1, 2, 3 }),
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        var bytes = await client.GetObjectBytesAsync("notes/a.txt");

        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public async Task GetObjectStringAsync_returns_utf8_body()
    {
        var payload = Encoding.UTF8.GetBytes("hi");
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = payload.Length,
                ResponseStream = new MemoryStream(payload),
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        Assert.Equal("hi", await client.GetObjectStringAsync("notes/a.txt"));
    }

    [Fact]
    public async Task GetObjectOrNullAsync_returns_null_when_not_found()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        Assert.Null(await client.GetObjectOrNullAsync("missing.txt"));
    }

    [Fact]
    public async Task CopyObjectAsync_applies_bucket_and_key_prefix()
    {
        CopyObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new CopyObjectResponse());

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        await client.CopyObjectAsync("notes/a.txt", "notes/b.txt", copy => copy.MetadataDirective = S3MetadataDirective.COPY);

        Assert.NotNull(captured);
        Assert.Equal("bucket", captured!.SourceBucket);
        Assert.Equal("bucket", captured.DestinationBucket);
        Assert.Equal("app/notes/a.txt", captured.SourceKey);
        Assert.Equal("app/notes/b.txt", captured.DestinationKey);
        Assert.Equal(S3MetadataDirective.COPY, captured.MetadataDirective);
    }

    [Fact]
    public async Task ObjectExistsAsync_returns_false_when_not_found()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);

        Assert.False(await client.ObjectExistsAsync("notes/a.txt"));
        amazon.Verify(
            x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.BucketName == "bucket" && r.Key == "app/notes/a.txt"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BucketExistsAsync_returns_false_when_missing()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListBucketsResponse
            {
                Buckets = new List<S3Bucket>
                {
                    new S3Bucket { BucketName = "other" },
                },
            });

        using var client = new S3Client(new S3ClientOptions { BucketName = "bucket" }, amazon.Object);

        Assert.False(await client.BucketExistsAsync());
    }

    [Fact]
    public async Task ListObjectsAsync_reads_one_page_with_a_default_limit()
    {
        ListObjectsV2Request? captured = null;
        var listed = new S3Object
        {
            Key = "app/notes/a.txt",
            Size = 42,
            LastModified = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            ETag = "\"abc\"",
        };
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = true,
                NextContinuationToken = "next",
                S3Objects = new List<S3Object> { listed },
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        var page = await client.ListObjectsAsync("notes/");

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/notes/", captured.Prefix);
        Assert.Equal(S3Client.DefaultMaxKeys, captured.MaxKeys);
        Assert.Null(captured.ContinuationToken);
        Assert.Null(captured.StartAfter);
        var info = Assert.Single(page.Objects);
        Assert.Equal("notes/a.txt", info.Key);
        Assert.Equal(42, info.Size);
        Assert.Equal(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero), info.LastModified);
        Assert.Equal("\"abc\"", info.ETag);
        Assert.Equal("app/notes/a.txt", listed.Key); // SDK object not mutated
        Assert.True(page.IsTruncated);
        Assert.Equal("next", page.NextContinuationToken);
        amazon.Verify(
            x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HeadObjectAsync_maps_to_object_response_without_body()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse
            {
                ContentLength = 99,
                LastModified = new DateTime(2024, 7, 2, 8, 0, 0, DateTimeKind.Utc),
                ETag = "\"etag\"",
                Headers = { ContentType = "text/plain" },
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        using var info = await client.HeadObjectAsync("notes/a.txt");

        Assert.Equal("notes/a.txt", info.Key);
        Assert.Equal(99, info.Size);
        Assert.Equal(new DateTimeOffset(2024, 7, 2, 8, 0, 0, TimeSpan.Zero), info.LastModified);
        Assert.Equal("\"etag\"", info.ETag);
        Assert.Equal("text/plain", info.ContentType);
        Assert.Equal("text/plain", info.Headers["Content-Type"]);
        Assert.Null(info.Body);
        amazon.Verify(
            x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.BucketName == "bucket" && r.Key == "app/notes/a.txt"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetObjectAsync_maps_get_to_object_response_with_body()
    {
        GetObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 3,
                ETag = "\"g\"",
                Headers = { ContentType = "application/octet-stream" },
                ResponseStream = new MemoryStream(new byte[] { 1, 2, 3 }),
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        using var response = await client.GetObjectAsync("notes/a.txt");

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/notes/a.txt", captured.Key);
        Assert.Equal("notes/a.txt", response.Key);
        Assert.Equal(3, response.Size);
        Assert.Equal("\"g\"", response.ETag);
        Assert.Equal("application/octet-stream", response.ContentType);
        Assert.Equal("application/octet-stream", response.Headers["Content-Type"]);
        Assert.NotNull(response.Body);
        Assert.Equal(new byte[] { 1, 2, 3 }, await ReadAllAsync(response.Body!));
    }

    [Fact]
    public async Task ListObjectsAsync_passes_max_keys_token_and_prefixed_start_after()
    {
        ListObjectsV2Request? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new ListObjectsV2Response { IsTruncated = false });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        var page = await client.ListObjectsAsync("notes/", maxKeys: 5, continuationToken: "token", startAfter: "notes/a.txt");

        Assert.Equal(5, captured!.MaxKeys);
        Assert.Equal("token", captured.ContinuationToken);
        Assert.Equal("app/notes/a.txt", captured.StartAfter);
        Assert.Empty(page.Objects);
        Assert.False(page.IsTruncated);
        Assert.Null(page.NextContinuationToken);
    }

    [Fact]
    public async Task ListObjectsAsync_rejects_a_max_keys_below_one()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ListObjectsAsync("notes/", maxKeys: 0));
        Assert.Equal("maxKeys", ex.ParamName);
    }

    [Fact]
    public async Task ListAllObjectsAsync_strips_prefix_and_follows_continuation()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.ContinuationToken == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = true,
                NextContinuationToken = "next",
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "app/notes/a.txt" },
                },
            });
        amazon
            .Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.ContinuationToken == "next"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = false,
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "app/notes/b.txt" },
                },
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        var keys = new List<string>();
        await foreach (var item in client.ListAllObjectsAsync("notes/"))
        {
            keys.Add(item.Key);
        }

        Assert.Equal(new[] { "notes/a.txt", "notes/b.txt" }, keys);
        amazon.Verify(
            x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.BucketName == "bucket" && r.Prefix == "app/notes/"),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ListAllObjectsAsync_sends_start_after_on_the_first_request_only()
    {
        var requests = new List<ListObjectsV2Request>();
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.ContinuationToken == null),
                It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = true,
                NextContinuationToken = "next",
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "app/notes/b.txt" },
                },
            });
        amazon
            .Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.ContinuationToken == "next"),
                It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((request, _) => requests.Add(request))
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = false,
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "app/notes/c.txt" },
                },
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        var keys = new List<string>();
        await foreach (var item in client.ListAllObjectsAsync("notes/", startAfter: "notes/a.txt"))
        {
            keys.Add(item.Key);
        }

        Assert.Equal(new[] { "notes/b.txt", "notes/c.txt" }, keys);
        Assert.Equal("app/notes/a.txt", requests[0].StartAfter);
        Assert.Null(requests[1].StartAfter);
    }

    [Fact]
    public async Task ListAllObjectsAsync_stops_requesting_when_the_caller_stops()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = true,
                NextContinuationToken = "next",
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "app/notes/a.txt" },
                },
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        await foreach (var item in client.ListAllObjectsAsync("notes/"))
        {
            Assert.Equal("notes/a.txt", item.Key);
            break;
        }

        amazon.Verify(
            x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PutObjectAsync_configure_runs_after_bucket_and_key_are_bound()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var client = new S3Client(PrefixOptions(), amazon.Object);
        await client.PutObjectAsync("a.txt", Encoding.UTF8.GetBytes("hi"), put =>
        {
            Assert.Equal("bucket", put.BucketName);
            Assert.Equal("app/a.txt", put.Key);
            put.ContentType = "text/plain";
        });

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/a.txt", captured.Key);
        Assert.Equal("text/plain", captured.ContentType);
        Assert.NotNull(captured.InputStream);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_applies_bucket_and_key_prefix()
    {
        InitiateMultipartUploadRequest? initiate = null;
        var uploadParts = new List<UploadPartRequest>();
        CompleteMultipartUploadRequest? complete = null;

        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InitiateMultipartUploadRequest, CancellationToken>((request, _) => initiate = request)
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UploadPartRequest, CancellationToken>((request, _) => uploadParts.Add(request))
            .ReturnsAsync(new UploadPartResponse { ETag = "\"part-1\"" });
        amazon
            .Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CompleteMultipartUploadRequest, CancellationToken>((request, _) => complete = request)
            .ReturnsAsync(new CompleteMultipartUploadResponse { ETag = "\"final\"", VersionId = "v-mp" });

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        var result = await client.PutObjectMultipartAsync(
            "notes/a.txt",
            stream,
            S3Client.MinimumMultipartPartLength,
            "text/csv");

        Assert.Equal("notes/a.txt", result.Key);
        Assert.Equal("\"final\"", result.ETag);
        Assert.Equal("v-mp", result.VersionId);
        Assert.Equal(5, result.ContentLength);
        Assert.Equal(1, result.PartCount);
        Assert.True(result.IsMultipart);

        Assert.NotNull(initiate);
        Assert.Equal("bucket", initiate!.BucketName);
        Assert.Equal("app/notes/a.txt", initiate.Key);
        Assert.Equal("text/csv", initiate.ContentType);

        var part = Assert.Single(uploadParts);
        Assert.Equal("bucket", part.BucketName);
        Assert.Equal("app/notes/a.txt", part.Key);
        Assert.Equal("upload-1", part.UploadId);
        Assert.Equal(1, part.PartNumber);
        Assert.Equal(5, part.PartSize);

        Assert.NotNull(complete);
        Assert.Equal("upload-1", complete!.UploadId);
        Assert.Single(complete.PartETags);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_uploads_multiple_parts_when_stream_exceeds_part_length()
    {
        var partSizes = new List<long>();
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UploadPartRequest, CancellationToken>((request, _) => partSizes.Add(request.PartSize ?? 0))
            .ReturnsAsync((UploadPartRequest request, CancellationToken _) =>
                new UploadPartResponse { ETag = $"\"part-{request.PartNumber}\"" });
        amazon
            .Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse { ETag = "\"final\"" });

        var partLength = S3Client.MinimumMultipartPartLength;
        var payload = new byte[partLength + 10];
        new Random(1).NextBytes(payload);
        using var stream = new MemoryStream(payload);
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectMultipartAsync("notes/big.bin", stream, partLength);

        Assert.Equal(new long[] { partLength, 10 }, partSizes);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_fills_next_part_while_previous_upload_is_in_flight()
    {
        var partLength = S3Client.MinimumMultipartPartLength;
        var payload = new byte[partLength * 2];
        new Random(2).NextBytes(payload);

        var firstUploadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstUpload = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPartReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (UploadPartRequest request, CancellationToken _) =>
            {
                if (request.PartNumber == 1)
                {
                    firstUploadStarted.TrySetResult(true);
                    await releaseFirstUpload.Task.ConfigureAwait(false);
                }

                return new UploadPartResponse { ETag = $"\"part-{request.PartNumber}\"" };
            });
        amazon
            .Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse { ETag = "\"final\"" });

        using var stream = new ReadNotifyStream(payload, partLength, secondPartReadStarted);
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        var putTask = client.PutObjectMultipartAsync("notes/big.bin", stream, partLength);

        await firstUploadStarted.Task;
        var secondPartRead = await Task.WhenAny(secondPartReadStarted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(secondPartReadStarted.Task, secondPartRead);

        releaseFirstUpload.TrySetResult(true);
        await putTask;
    }

    [Fact]
    public async Task PutObjectMultipartAsync_empty_stream_uploads_one_zero_length_part()
    {
        long? partSize = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UploadPartRequest, CancellationToken>((request, _) => partSize = request.PartSize)
            .ReturnsAsync(new UploadPartResponse { ETag = "\"part-1\"" });
        amazon
            .Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse { ETag = "\"final\"" });

        using var stream = new MemoryStream();
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectMultipartAsync("notes/empty.bin", stream, S3Client.MinimumMultipartPartLength);

        Assert.Equal(0, partSize);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_aborts_when_upload_part_fails()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("boom"));
        amazon
            .Setup(x => x.AbortMultipartUploadAsync(It.IsAny<AbortMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbortMultipartUploadResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            client.PutObjectMultipartAsync("notes/a.txt", stream, S3Client.MinimumMultipartPartLength));

        amazon.Verify(
            x => x.AbortMultipartUploadAsync(
                It.Is<AbortMultipartUploadRequest>(r =>
                    r.BucketName == "bucket"
                    && r.Key == "app/notes/a.txt"
                    && r.UploadId == "upload-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        amazon.Verify(
            x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_rejects_part_length_below_s3_minimum()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        using var stream = new MemoryStream(new byte[] { 1 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.PutObjectMultipartAsync("notes/a.txt", stream, S3Client.MinimumMultipartPartLength - 1));

        Assert.Equal("partLength", ex.ParamName);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_does_not_dispose_caller_stream()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPartResponse { ETag = "\"part-1\"" });
        amazon
            .Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse { ETag = "\"final\"" });

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectMultipartAsync("notes/a.txt", stream, S3Client.MinimumMultipartPartLength);

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_configure_runs_after_bucket_and_key_are_bound()
    {
        InitiateMultipartUploadRequest? initiate = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InitiateMultipartUploadRequest, CancellationToken>((request, _) => initiate = request)
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadPartResponse { ETag = "\"part-1\"" });
        amazon
            .Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse { ETag = "\"final\"" });

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hi"));
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectMultipartAsync(
            "a.txt",
            stream,
            S3Client.MinimumMultipartPartLength,
            initiateRequest =>
            {
                Assert.Equal("bucket", initiateRequest.BucketName);
                Assert.Equal("app/a.txt", initiateRequest.Key);
                initiateRequest.ContentType = "text/plain";
            });

        Assert.Equal("text/plain", initiate!.ContentType);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_rejects_part_length_above_int_max_value()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        using var stream = new MemoryStream(new byte[] { 1 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.PutObjectMultipartAsync("notes/a.txt", stream, (long)int.MaxValue + 1));

        Assert.Equal("partLength", ex.ParamName);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_aborts_with_none_token_when_cancelled_during_upload()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken? abortToken = null;

        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .Returns((UploadPartRequest _, CancellationToken _) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });
        amazon
            .Setup(x => x.AbortMultipartUploadAsync(It.IsAny<AbortMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AbortMultipartUploadRequest, CancellationToken>((_, token) => abortToken = token)
            .ReturnsAsync(new AbortMultipartUploadResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.PutObjectMultipartAsync("notes/a.txt", stream, S3Client.MinimumMultipartPartLength, cts.Token));

        Assert.Equal(CancellationToken.None, abortToken);
        amazon.Verify(
            x => x.AbortMultipartUploadAsync(
                It.Is<AbortMultipartUploadRequest>(r => r.UploadId == "upload-1"),
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_throws_when_part_count_exceeds_s3_limit()
    {
        var uploadCount = 0;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1" });
        amazon
            .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => uploadCount++)
            .ReturnsAsync((UploadPartRequest request, CancellationToken _) =>
                new UploadPartResponse { ETag = $"\"part-{request.PartNumber}\"" });
        amazon
            .Setup(x => x.AbortMultipartUploadAsync(It.IsAny<AbortMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbortMultipartUploadResponse());

        // One byte past MaxMultipartParts full parts forces the ceiling check on the next fill.
        using var stream = new SyntheticPartsStream(S3Client.MinimumMultipartPartLength, S3Client.MaxMultipartParts + 1);
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PutObjectMultipartAsync("notes/huge.bin", stream, S3Client.MinimumMultipartPartLength));

        Assert.Contains(S3Client.MaxMultipartParts.ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Equal(S3Client.MaxMultipartParts, uploadCount);
        amazon.Verify(
            x => x.AbortMultipartUploadAsync(It.IsAny<AbortMultipartUploadRequest>(), CancellationToken.None),
            Times.Once);
        amazon.Verify(
            x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteObjectAsync_applies_bucket_and_key_prefix()
    {
        DeleteObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new DeleteObjectResponse());

        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.DeleteObjectAsync("notes/a.txt", delete => delete.VersionId = "v1");

        Assert.NotNull(captured);
        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/notes/a.txt", captured.Key);
        Assert.Equal("v1", captured.VersionId);
    }

    [Fact]
    public async Task ObjectExistsAsync_returns_true_when_head_succeeds()
    {
        GetObjectMetadataRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetObjectMetadataRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new GetObjectMetadataResponse());

        using var client = new S3Client(PrefixOptions(), amazon.Object);

        Assert.True(await client.ObjectExistsAsync("notes/a.txt"));
        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/notes/a.txt", captured.Key);
    }

    [Fact]
    public async Task BucketExistsAsync_returns_true_when_bucket_is_listed()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListBucketsResponse
            {
                Buckets = new List<S3Bucket> { new() { BucketName = "bucket" } },
            });

        using var client = new S3Client(PrefixOptions(), amazon.Object);

        Assert.True(await client.BucketExistsAsync());
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static S3ClientOptions PrefixOptions() => new()
    {
        BucketName = "bucket",
        KeyPrefix = "app",
    };

    /// <summary>
    /// Signals when reading advances past the first <paramref name="firstPartLength"/> bytes.
    /// </summary>
    private sealed class ReadNotifyStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly long _firstPartLength;
        private readonly TaskCompletionSource<bool> _pastFirstPart;
        private long _totalRead;

        public ReadNotifyStream(byte[] payload, long firstPartLength, TaskCompletionSource<bool> pastFirstPart)
        {
            _inner = new MemoryStream(payload);
            _firstPartLength = firstPartLength;
            _pastFirstPart = pastFirstPart;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            NoteRead(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            NoteRead(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void NoteRead(int read)
        {
            if (read <= 0)
            {
                return;
            }

            _totalRead += read;
            if (_totalRead > _firstPartLength)
            {
                _pastFirstPart.TrySetResult(true);
            }
        }
    }

    /// <summary>
    /// Yields <paramref name="partCount"/> zero-filled parts of <paramref name="partLength"/> bytes without allocating the full payload.
    /// </summary>
    private sealed class SyntheticPartsStream : Stream
    {
        private readonly int _partLength;
        private readonly int _partCount;
        private int _partsEmitted;
        private int _offsetInPart;

        public SyntheticPartsStream(long partLength, int partCount)
        {
            if (partLength < 1 || partLength > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(partLength));
            }

            _partLength = (int)partLength;
            _partCount = partCount;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => (long)_partLength * _partCount;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_partsEmitted >= _partCount)
            {
                return 0;
            }

            var remainingInPart = _partLength - _offsetInPart;
            var toCopy = Math.Min(count, remainingInPart);
            Array.Clear(buffer, offset, toCopy);
            _offsetInPart += toCopy;
            if (_offsetInPart >= _partLength)
            {
                _partsEmitted++;
                _offsetInPart = 0;
            }

            return toCopy;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
