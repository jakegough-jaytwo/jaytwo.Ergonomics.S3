using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using jaytwo.Ergonomics.S3.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class S3ClientLoggingTests
{
    [Fact]
    public async Task PutObjectAsync_emits_object_put_event()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { ETag = "\"abc\"" });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        await client.PutObjectAsync("notes/hello.txt", Encoding.UTF8.GetBytes("hi"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(EventIds.ObjectEvents.ObjectPut.Name, entry.EventId.Name);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal("notes/hello.txt", entry.Fields["key"]);
        Assert.Equal("bucket", entry.Fields["bucket"]);
        Assert.Equal("qa1/", entry.Fields["key_prefix"]);
        Assert.Equal(S3Client.ActivityNames.PutObject, entry.Fields["s3_operation"]);
        Assert.Matches(@"] Put object in \S+ \(2 B\)\.$", entry.Message);
        Assert.False(entry.Fields.ContainsKey("operation_id"));
    }

    [Fact]
    public async Task PutObjectAsync_unknown_length_renders_unknown_size()
    {
        // arrange
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { ETag = "\"abc\"" });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        // act
        await client.PutObjectAsync("notes/hello.txt", new ThrowingReadStream(), contentLength: null);

        // assert
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(EventIds.ObjectEvents.ObjectPut.Name, entry.EventId.Name);
        Assert.Matches(@"] Put object in \S+ \(unknown size\)\.$", entry.Message);
        Assert.Null(entry.Fields["content_length"]);
    }

    [Fact]
    public async Task PutObjectMultipartAsync_emits_object_multipart_put_event()
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

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hi"));

        await client.PutObjectMultipartAsync("notes/hello.txt", stream, S3Client.MinimumMultipartPartLength);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(EventIds.ObjectEvents.ObjectMultipartPut.Name, entry.EventId.Name);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal("notes/hello.txt", entry.Fields["key"]);
        Assert.Equal(S3Client.ActivityNames.PutObjectMultipart, entry.Fields["s3_operation"]);
        Assert.Equal(2L, Convert.ToInt64(entry.Fields["content_length"]));
        Assert.Equal(1, Convert.ToInt32(entry.Fields["part_count"]));
        Assert.Matches(@"] Put object in \S+ \(2 B in 1 parts\)\.$", entry.Message);
        Assert.False(entry.Fields.ContainsKey("operation_id"));
    }

    [Fact]
    public async Task ObjectExistsAsync_not_found_emits_object_not_found_not_failure()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        var exists = await client.ObjectExistsAsync("missing.txt");

        Assert.False(exists);
        var notFound = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectNotFound.Name);
        Assert.Matches(@"] Object not found after \S+\.$", notFound.Message);
        Assert.Equal(false, notFound.Fields["object_exists"]);
        Assert.Equal(S3Client.ActivityNames.ObjectExists, notFound.Fields["s3_operation"]);
        Assert.DoesNotContain(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name);
    }

    [Fact]
    public async Task GetObjectAsync_failure_emits_operation_failed()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("boom")
            {
                StatusCode = HttpStatusCode.InternalServerError,
                ErrorCode = "InternalError",
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        await Assert.ThrowsAsync<AmazonS3Exception>(() => client.GetObjectAsync("notes/hello.txt"));

        var failed = Assert.Single(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name
            && e.Level == LogLevel.Error);
        Assert.Matches(@"] s3\.get_object\.headers failed after \S+\.$", failed.Message);
        Assert.DoesNotContain(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name);
    }

    [Fact]
    public async Task GetObjectAsync_emits_get_headers_then_get_on_dispose()
    {
        var body = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 5,
                ResponseStream = body,
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        var response = await client.GetObjectAsync("notes/hello.txt");

        var getHeaders = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGetHeaders.Name);
        Assert.Equal(S3Client.ActivityNames.GetObjectHeaders, getHeaders.Fields["s3_operation"]);
        Assert.Matches(@"] Received object headers in \S+\.$", getHeaders.Message);
        Assert.DoesNotContain(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name);

        var buffer = new byte[5];
        var read = await response.Body!.ReadAsync(buffer, 0, buffer.Length);
        Assert.Equal(5, read);

        response.Dispose();

        var get = Assert.Single(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name);
        Assert.Equal(LogLevel.Debug, get.Level);
        Assert.Equal(S3Client.ActivityNames.GetObjectUse, get.Fields["s3_operation"]);
        Assert.Equal(5L, Convert.ToInt64(get.Fields["bytes_read"]));
        Assert.Equal(5L, Convert.ToInt64(get.Fields["content_length"]));
        Assert.Matches(@"] Read object in \S+ \(5 B\)\.$", get.Message);
        Assert.Equal("notes/hello.txt", get.Fields["key"]);
        Assert.Equal(
            logger.Entries.Find(e => e.EventId.Name == EventIds.ObjectEvents.ObjectGetHeaders.Name)!.Fields["operation_id"],
            get.Fields["operation_id"]);
        Assert.False(getHeaders.Fields.ContainsKey("range_start"));
        Assert.False(get.Fields.ContainsKey("range_start"));
        Assert.False(get.Fields.ContainsKey("object_size"));
        Assert.False(getHeaders.Fields.ContainsKey("byte_range"));
        Assert.False(get.Fields.ContainsKey("byte_range"));
    }

    [Fact]
    public async Task GetObjectAsync_range_adds_range_fields_without_changing_the_message()
    {
        // arrange
        var body = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 4,
                ContentRange = "bytes 0-3/10",
                ResponseStream = body,
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        // act
        var response = await client.GetObjectAsync("notes/hello.txt", new S3ByteRange(0, 4));
        await response.Body!.ReadAsync(new byte[4], 0, 4);
        response.Dispose();

        // assert
        var getHeaders = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGetHeaders.Name);
        Assert.Matches(@"] Received object headers in \S+\.$", getHeaders.Message);
        Assert.Equal(4L, Convert.ToInt64(getHeaders.Fields["content_length"]));
        Assert.Equal(0L, Convert.ToInt64(getHeaders.Fields["range_start"]));
        Assert.Equal(3L, Convert.ToInt64(getHeaders.Fields["range_end"]));
        Assert.Equal(10L, Convert.ToInt64(getHeaders.Fields["object_size"]));
        Assert.Equal("bytes=0-3", getHeaders.Fields["byte_range"]);

        var get = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name);
        Assert.Matches(@"] Read object in \S+ \(4 B\)\.$", get.Message);
        Assert.Equal(4L, Convert.ToInt64(get.Fields["bytes_read"]));
        Assert.Equal(4L, Convert.ToInt64(get.Fields["content_length"]));
        Assert.Equal(0L, Convert.ToInt64(get.Fields["range_start"]));
        Assert.Equal(3L, Convert.ToInt64(get.Fields["range_end"]));
        Assert.Equal(10L, Convert.ToInt64(get.Fields["object_size"]));
        Assert.Equal("bytes=0-3", get.Fields["byte_range"]);
    }

    [Fact]
    public async Task GetObjectAsync_range_omits_object_size_when_the_total_is_unknown()
    {
        // arrange
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 2,
                ContentRange = "bytes 0-1/*",
                ResponseStream = new MemoryStream(new byte[] { 1, 2 }),
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        // act
        using var response = await client.GetObjectAsync("notes/hello.txt", new S3ByteRange(0, 2));

        // assert
        var getHeaders = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGetHeaders.Name);
        Assert.Equal(0L, Convert.ToInt64(getHeaders.Fields["range_start"]));
        Assert.Equal(1L, Convert.ToInt64(getHeaders.Fields["range_end"]));
        Assert.False(getHeaders.Fields.ContainsKey("object_size"));
        Assert.Equal("bytes=0-1", getHeaders.Fields["byte_range"]);
        Assert.Null(response.ContentRange!.Value.ObjectSize);
    }

    [Fact]
    public async Task GetObjectAsync_shortened_range_logs_the_request_and_the_returned_bounds()
    {
        // arrange
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 2,
                ContentRange = "bytes 8-9/10",
                ResponseStream = new MemoryStream(new byte[] { 8, 9 }),
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        // act
        using var response = await client.GetObjectAsync("notes/hello.txt", new S3ByteRange(8, 100));

        // assert
        var getHeaders = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGetHeaders.Name);
        Assert.Equal("bytes=8-107", getHeaders.Fields["byte_range"]);
        Assert.Equal(8L, Convert.ToInt64(getHeaders.Fields["range_start"]));
        Assert.Equal(9L, Convert.ToInt64(getHeaders.Fields["range_end"]));
        Assert.Equal(10L, Convert.ToInt64(getHeaders.Fields["object_size"]));
        Assert.Equal(2, response.Size);
    }

    [Fact]
    public async Task GetObjectAsync_configure_overwrite_logs_the_header_that_was_sent()
    {
        // arrange
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 2,
                ContentRange = "bytes 5-6/10",
                ResponseStream = new MemoryStream(new byte[] { 5, 6 }),
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        // act
        using var response = await client.GetObjectAsync(
            "notes/hello.txt",
            new S3ByteRange(0, 10),
            get => get.ByteRange = new ByteRange(5, 6));

        // assert
        var getHeaders = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGetHeaders.Name);
        Assert.Equal("bytes=5-6", getHeaders.Fields["byte_range"]);
        Assert.Equal(5L, Convert.ToInt64(getHeaders.Fields["range_start"]));
        Assert.Equal(6L, Convert.ToInt64(getHeaders.Fields["range_end"]));
        Assert.Equal(2, response.Size);
    }

    [Fact]
    public async Task GetObjectOrNullAsync_range_not_found_logs_the_request_range()
    {
        // arrange
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        // act
        var response = await client.GetObjectOrNullAsync("missing.txt", new S3ByteRange(0, 4));

        // assert
        Assert.Null(response);
        var notFound = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectNotFound.Name);
        Assert.Equal(LogLevel.Debug, notFound.Level);
        Assert.Equal("bytes=0-3", notFound.Fields["byte_range"]);
        Assert.False(notFound.Fields.ContainsKey("range_start"));
        Assert.Matches(@"] Object not found after \S+\.$", notFound.Message);
        Assert.DoesNotContain(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name);
    }

    [Fact]
    public async Task GetObjectOrNullAsync_unsatisfiable_range_logs_the_request_range_as_a_failure()
    {
        // arrange
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("The requested range is not satisfiable")
            {
                StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable,
                ErrorCode = "InvalidRange",
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        // act
        var ex = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            client.GetObjectOrNullAsync("notes/hello.txt", new S3ByteRange(100, 1)));

        // assert
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, ex.StatusCode);
        var failed = Assert.Single(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name);
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Equal("bytes=100-100", failed.Fields["byte_range"]);
        Assert.False(failed.Fields.ContainsKey("range_start"));
        Assert.Matches(@"] s3\.get_object\.headers failed after \S+\.$", failed.Message);
    }

    [Fact]
    public async Task GetObjectAsync_stream_read_failure_lands_on_use_phase()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 10,
                ResponseStream = new ThrowingReadStream(),
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        var response = await client.GetObjectAsync("notes/hello.txt");
        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                response.Body!.ReadAsync(new byte[4], 0, 4));
        }
        finally
        {
            // Dispose completes the use phase after the read fault.
            response.Dispose();
        }

        Assert.Contains(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGetHeaders.Name);
        Assert.Contains(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name
            && e.Fields["s3_operation"] as string == S3Client.ActivityNames.GetObjectUse
            && e.Level == LogLevel.Error);
        Assert.DoesNotContain(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name);
    }

    [Fact]
    public async Task Cancelled_token_emits_cancelled_warning()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Returns<PutObjectRequest, CancellationToken>((_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new PutObjectResponse());
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.PutObjectAsync("notes/hello.txt", Encoding.UTF8.GetBytes("hi"), cts.Token));

        var cancelled = Assert.Single(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.Cancelled.Name
            && e.Level == LogLevel.Warning);
        Assert.Matches(@"] s3\.put_object cancelled after \S+\.$", cancelled.Message);
    }

    [Fact]
    public async Task GetObjectOrNullAsync_not_found_emits_object_not_found_not_failure()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        var response = await client.GetObjectOrNullAsync("missing.txt");

        Assert.Null(response);
        var notFound = Assert.Single(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectNotFound.Name);
        Assert.Equal(LogLevel.Debug, notFound.Level);
        Assert.Matches(@"] Object not found after \S+\.$", notFound.Message);
        Assert.Equal(S3Client.ActivityNames.GetObjectHeaders, notFound.Fields["s3_operation"]);
        Assert.Equal("missing.txt", notFound.Fields["key"]);
        Assert.False(notFound.Fields.ContainsKey("operation_id"));
        Assert.DoesNotContain(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name);
    }

    [Fact]
    public async Task GetObjectBytesAsync_drains_stream_and_emits_get_headers_then_get()
    {
        var body = new MemoryStream(new byte[] { 9, 8, 7 });
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ContentLength = 3,
                ResponseStream = body,
            });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        var bytes = await client.GetObjectBytesAsync("notes/hello.txt");

        Assert.Equal(new byte[] { 9, 8, 7 }, bytes);
        Assert.Contains(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGetHeaders.Name);
        var get = Assert.Single(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name);
        Assert.Equal(3L, Convert.ToInt64(get.Fields["bytes_read"]));
    }

    [Fact]
    public async Task CopyObjectAsync_emits_object_copied_with_prefixed_keys()
    {
        CopyObjectRequest? captured = null;
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new CopyObjectResponse { ETag = "\"etag\"" });

        var logger = new CapturingLogger();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger);

        await client.CopyObjectAsync("notes/a.txt", "notes/b.txt");

        Assert.NotNull(captured);
        Assert.Equal("bucket", captured!.SourceBucket);
        Assert.Equal("bucket", captured.DestinationBucket);
        Assert.Equal("qa1/notes/a.txt", captured.SourceKey);
        Assert.Equal("qa1/notes/b.txt", captured.DestinationKey);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(EventIds.ObjectEvents.ObjectCopied.Name, entry.EventId.Name);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal("notes/a.txt", entry.Fields["source_key"]);
        Assert.Equal("notes/b.txt", entry.Fields["key"]);
        Assert.Equal(S3Client.ActivityNames.CopyObject, entry.Fields["s3_operation"]);
        Assert.False(entry.Fields.ContainsKey("operation_id"));
    }

    [Theory]
    [InlineData(true, "found")]
    [InlineData(false, "not found")]
    public void LogBucketExists_renders_result_in_message(bool exists, string expected)
    {
        // arrange
        var logger = new CapturingLogger();
        var eventLogger = new S3ObjectEventLogger(logger);

        // act
        eventLogger.LogBucketExists("bucket", TimeSpan.FromMilliseconds(5), exists);

        // assert
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(EventIds.ObjectEvents.BucketExists.Name, entry.EventId.Name);
        Assert.Matches($@"] Checked bucket in \S+ \({expected}\)\.$", entry.Message);
        Assert.Equal(exists, entry.Fields["bucket_exists"]);
    }

    [Fact]
    public void LogObjectRead_reports_total_with_phases_on_fields()
    {
        // arrange
        var logger = new CapturingLogger();
        var eventLogger = new S3ObjectEventLogger(logger);

        // act
        eventLogger.LogObjectGet(
            "bucket",
            null,
            "notes/hello.txt",
            TimeSpan.FromMilliseconds(3.4),
            TimeSpan.FromMilliseconds(0.9),
            29 * 1024,
            29 * 1024);

        // assert
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(EventIds.ObjectEvents.ObjectGet.Name, entry.EventId.Name);
        Assert.Matches(@"] Read object in 4\.3ms \(29 KiB\)\.$", entry.Message);
        Assert.Equal("0.0043", entry.Fields["elapsed_time_seconds"]);
        Assert.Equal("0.0034", entry.Fields["headers_elapsed_time_seconds"]);
        Assert.Equal("0.0009", entry.Fields["stream_elapsed_time_seconds"]);
    }

    private static S3ClientOptions PrefixOptions() => new()
    {
        BucketName = "bucket",
        KeyPrefix = "qa1/",
    };

    private sealed class ThrowingReadStream : Stream
    {
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
            => throw new IOException("boom");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly AsyncLocal<Scope?> _currentScope = new();

        public List<LogEntry> Entries { get; } = new();

        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            var scope = new Scope(this, state!, _currentScope.Value);
            _currentScope.Value = scope;
            return scope;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            var scopes = new Stack<Scope>();

            for (var scope = _currentScope.Value; scope is not null; scope = scope.Parent)
            {
                scopes.Push(scope);
            }

            while (scopes.Count > 0)
            {
                if (scopes.Pop().State is IEnumerable<KeyValuePair<string, object?>> items)
                {
                    foreach (var item in items)
                    {
                        fields[item.Key] = item.Value;
                    }
                }
            }

            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), fields, exception));
        }

        private void RestoreScope(Scope? scope) => _currentScope.Value = scope;

        private sealed class Scope : IDisposable
        {
            private readonly CapturingLogger _logger;

            public Scope(CapturingLogger logger, object state, Scope? parent)
            {
                _logger = logger;
                State = state;
                Parent = parent;
            }

            public Scope? Parent { get; }

            public object State { get; }

            public void Dispose() => _logger.RestoreScope(Parent);
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Dictionary<string, object?> Fields,
        Exception? Exception);
}
