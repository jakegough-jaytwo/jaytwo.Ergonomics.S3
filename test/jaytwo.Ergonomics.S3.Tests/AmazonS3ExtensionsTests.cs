using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class AmazonS3ExtensionsTests
{
    [Fact]
    public async Task PutObjectAsync_configure_sets_auto_close_stream_false()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hi"));
        await amazon.Object.PutObjectAsync(request =>
        {
            request.BucketName = "bucket";
            request.Key = "a.txt";
            request.InputStream = stream;
        });

        Assert.NotNull(captured);
        Assert.False(captured!.AutoCloseStream);
        Assert.Equal("bucket", captured.BucketName);
        Assert.Equal("a.txt", captured.Key);
        Assert.Same(stream, captured.InputStream);
    }

    [Fact]
    public async Task PutObjectAsync_bucket_key_stream_binds_request()
    {
        PutObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new PutObjectResponse());

        using var stream = new MemoryStream(new byte[] { 1, 2 });
        await amazon.Object.PutObjectAsync("bucket", "key.bin", stream, put => put.ContentType = "application/octet-stream");

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("key.bin", captured.Key);
        Assert.Same(stream, captured.InputStream);
        Assert.Equal("application/octet-stream", captured.ContentType);
        Assert.False(captured.AutoCloseStream);
    }

    [Fact]
    public async Task GetObjectAsync_configure_forwards_request()
    {
        GetObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new GetObjectResponse());

        await amazon.Object.GetObjectAsync(request =>
        {
            request.BucketName = "bucket";
            request.Key = "a.txt";
        });

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("a.txt", captured.Key);
    }

    [Fact]
    public async Task HeadObjectAsync_configure_calls_get_object_metadata()
    {
        GetObjectMetadataRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetObjectMetadataRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new GetObjectMetadataResponse());

        await amazon.Object.HeadObjectAsync(request =>
        {
            request.BucketName = "bucket";
            request.Key = "a.txt";
        });

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("a.txt", captured.Key);
    }

    [Fact]
    public async Task DeleteObjectAsync_configure_forwards_request()
    {
        DeleteObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new DeleteObjectResponse());

        await amazon.Object.DeleteObjectAsync(request =>
        {
            request.BucketName = "bucket";
            request.Key = "a.txt";
        });

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("a.txt", captured.Key);
    }

    [Fact]
    public async Task ListObjectsV2Async_configure_forwards_request()
    {
        ListObjectsV2Request? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new ListObjectsV2Response());

        await amazon.Object.ListObjectsV2Async(request =>
        {
            request.BucketName = "bucket";
            request.Prefix = "app/";
            request.MaxKeys = 10;
        });

        Assert.Equal("bucket", captured!.BucketName);
        Assert.Equal("app/", captured.Prefix);
        Assert.Equal(10, captured.MaxKeys);
    }

    [Fact]
    public async Task BucketExistsAsync_returns_true_when_name_matches()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListBucketsResponse
            {
                Buckets = new List<S3Bucket>
                {
                    new() { BucketName = "other" },
                    new() { BucketName = "bucket" },
                },
            });

        Assert.True(await amazon.Object.BucketExistsAsync("bucket"));
    }

    [Fact]
    public async Task BucketExistsAsync_returns_false_when_missing_or_null_list()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListBucketsResponse { Buckets = null });

        Assert.False(await amazon.Object.BucketExistsAsync("bucket"));

        amazon
            .Setup(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListBucketsResponse
            {
                Buckets = new List<S3Bucket> { new() { BucketName = "other" } },
            });

        Assert.False(await amazon.Object.BucketExistsAsync("bucket"));
    }

    [Fact]
    public async Task BucketExistsAsync_rejects_empty_bucket_name()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => amazon.Object.BucketExistsAsync(string.Empty));
        Assert.Equal("bucketName", ex.ParamName);
    }

    [Fact]
    public async Task ObjectExistsAsync_returns_true_when_head_succeeds()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());

        Assert.True(await amazon.Object.ObjectExistsAsync("bucket", "a.txt"));
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

        Assert.False(await amazon.Object.ObjectExistsAsync("bucket", "missing.txt"));
    }

    [Fact]
    public async Task ObjectExistsAsync_rethrows_non_not_found()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("denied")
            {
                StatusCode = HttpStatusCode.Forbidden,
                ErrorCode = "AccessDenied",
            });

        await Assert.ThrowsAsync<AmazonS3Exception>(() => amazon.Object.ObjectExistsAsync("bucket", "a.txt"));
    }

    [Fact]
    public void PutObjectAsync_configure_requires_client_and_configure()
    {
        IAmazonS3? client = null;
        Assert.Throws<ArgumentNullException>((Action)(() => client!.PutObjectAsync(_ => { })));

        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        Assert.Throws<ArgumentNullException>((Action)(() =>
            amazon.Object.PutObjectAsync((Action<PutObjectRequest>)null!)));
    }
}
