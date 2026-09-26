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
        Assert.False(entry.Fields.ContainsKey("operation_id"));
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
        Assert.Contains(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectNotFound.Name);
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

        Assert.Contains(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name
            && e.Level == LogLevel.Error);
        Assert.DoesNotContain(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectStreamClosed.Name);
    }

    [Fact]
    public async Task GetObjectAsync_emits_headers_then_stream_closed_on_dispose()
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

        Assert.Contains(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name
            && e.Fields["s3_operation"] as string == S3Client.ActivityNames.GetObject);
        Assert.DoesNotContain(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectStreamClosed.Name);

        var buffer = new byte[5];
        var read = await response.Body!.ReadAsync(buffer, 0, buffer.Length);
        Assert.Equal(5, read);

        response.Dispose();

        var closed = Assert.Single(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectStreamClosed.Name);
        Assert.Equal(LogLevel.Debug, closed.Level);
        Assert.Equal(S3Client.ActivityNames.GetObjectUse, closed.Fields["s3_operation"]);
        Assert.Equal(5L, Convert.ToInt64(closed.Fields["bytes_read"]));
        Assert.Equal("notes/hello.txt", closed.Fields["key"]);
        Assert.Equal(
            logger.Entries.Find(e => e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name)!.Fields["operation_id"],
            closed.Fields["operation_id"]);
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

        Assert.Contains(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name);
        Assert.Contains(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name
            && e.Fields["s3_operation"] as string == S3Client.ActivityNames.GetObjectUse
            && e.Level == LogLevel.Error);
        Assert.DoesNotContain(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectStreamClosed.Name);
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

        Assert.Contains(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.Cancelled.Name
            && e.Level == LogLevel.Warning);
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
        Assert.Equal(S3Client.ActivityNames.GetObject, notFound.Fields["s3_operation"]);
        Assert.Equal("missing.txt", notFound.Fields["key"]);
        Assert.False(notFound.Fields.ContainsKey("operation_id"));
        Assert.DoesNotContain(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.OperationFailed.Name);
    }

    [Fact]
    public async Task GetObjectBytesAsync_drains_stream_and_emits_get_then_closed()
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
        Assert.Contains(logger.Entries, e => e.EventId.Name == EventIds.ObjectEvents.ObjectGet.Name);
        var closed = Assert.Single(logger.Entries, e =>
            e.EventId.Name == EventIds.ObjectEvents.ObjectStreamClosed.Name);
        Assert.Equal(3L, Convert.ToInt64(closed.Fields["bytes_read"]));
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

            Entries.Add(new LogEntry(logLevel, eventId, fields, exception));
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
        Dictionary<string, object?> Fields,
        Exception? Exception);
}
