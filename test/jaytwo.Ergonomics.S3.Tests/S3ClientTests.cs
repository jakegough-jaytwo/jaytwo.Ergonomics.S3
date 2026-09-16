using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync("notes/a.txt", stream, put => put.ContentType = "text/plain");

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
    public async Task PutObjectAsync_stream_with_md5_sets_content_length_when_provided()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        var md5 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync("notes/a.txt", stream, contentLength: 3, md5);

        Assert.NotNull(captured);
        Assert.Same(stream, captured!.InputStream);
        Assert.Equal(3, captured.Headers.ContentLength);
        Assert.Equal(Convert.ToBase64String(md5), captured.MD5Digest);
    }

    [Fact]
    public async Task PutObjectAsync_stream_with_md5_skips_content_length_when_null()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        var md5 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync("notes/a.txt", stream, contentLength: null, md5);

        Assert.NotNull(captured);
        Assert.Same(stream, captured!.InputStream);
        Assert.Equal(-1, captured.Headers.ContentLength);
        Assert.Equal(Convert.ToBase64String(md5), captured.MD5Digest);
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
        Assert.NotNull(response);
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
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((request, _) => captured = request)
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
        var page = await client.ListObjectsAsync("notes/");

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/notes/", captured.Prefix);
        Assert.Equal(S3Client.DefaultMaxKeys, captured.MaxKeys);
        Assert.Null(captured.ContinuationToken);
        Assert.Null(captured.StartAfter);
        Assert.Equal(new[] { "notes/a.txt" }, page.Objects.Select(x => x.Key));
        Assert.True(page.IsTruncated);
        Assert.Equal("next", page.NextContinuationToken);
        amazon.Verify(
            x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()),
            Times.Once);
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
        await client.PutObjectAsync("a.txt", "hi", put =>
        {
            Assert.Equal("bucket", put.BucketName);
            Assert.Equal("app/a.txt", put.Key);
            put.ContentType = "text/plain";
        });

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/a.txt", captured.Key);
        Assert.Equal("text/plain", captured.ContentType);
        Assert.Equal("hi", captured.ContentBody);
    }

    private static S3ClientOptions PrefixOptions() => new()
    {
        BucketName = "bucket",
        KeyPrefix = "app",
    };
}
