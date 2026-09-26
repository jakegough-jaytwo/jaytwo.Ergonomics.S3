using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using jaytwo.Ergonomics.S3.Tracing;
using Moq;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class S3ClientTelemetryTests
{
    [Fact]
    public async Task PutObjectAsync_records_success_activity_and_duration_metric()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { ETag = "\"abc\"" });

        using var telemetry = new TelemetryCapture();
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.PutObjectAsync("notes/hello.txt", Encoding.UTF8.GetBytes("hi"));

        var activities = telemetry.SnapshotActivities();
        var durations = telemetry.SnapshotDurations();
        var errors = telemetry.SnapshotErrors();

        var activity = Assert.Single(activities, a => a.OperationName == S3Client.ActivityNames.PutObject);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("bucket", activity.GetTagItem("aws.s3.bucket"));
        Assert.Equal("qa1/", activity.GetTagItem("aws.s3.key_prefix"));
        Assert.Equal("notes/hello.txt", activity.GetTagItem("aws.s3.key"));
        Assert.Null(activity.GetTagItem("s3.operation_id"));

        var duration = Assert.Single(
            durations,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.PutObject)
                && !HasTag(m.Tags, S3Metrics.ErrorTypeTagName));
        Assert.True(duration.Value >= 0);
        Assert.DoesNotContain(errors, _ => true);
        Assert.DoesNotContain(durations, m => HasTag(m.Tags, "aws.s3.key") || HasTag(m.Tags, "s3.operation_id"));
    }

    [Fact]
    public async Task PutObjectAsync_cancelled_records_cancelled_error_metric()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var telemetry = new TelemetryCapture();
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.PutObjectAsync("notes/hello.txt", Encoding.UTF8.GetBytes("hi"), cts.Token));

        var activities = telemetry.SnapshotActivities();
        var durations = telemetry.SnapshotDurations();
        var errors = telemetry.SnapshotErrors();

        var activity = Assert.Single(activities, a => a.OperationName == S3Client.ActivityNames.PutObject);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);

        Assert.Contains(
            errors,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.PutObject)
                && TagEquals(m.Tags, S3Metrics.ErrorTypeTagName, S3Metrics.CancelledErrorType));
        Assert.Contains(
            durations,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.PutObject)
                && TagEquals(m.Tags, S3Metrics.ErrorTypeTagName, S3Metrics.CancelledErrorType));
    }

    [Fact]
    public async Task GetObjectAsync_splits_headers_and_use_activities_with_shared_operation_id()
    {
        var body = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                BucketName = "bucket",
                Key = "qa1/notes/hello.txt",
                ContentLength = 5,
                ResponseStream = body,
            });

        using var telemetry = new TelemetryCapture();
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        using (var response = await client.GetObjectAsync("notes/hello.txt"))
        {
            Assert.NotNull(response.Body);
            await response.Body!.CopyToAsync(Stream.Null);
        }

        var activities = telemetry.SnapshotActivities();
        var durations = telemetry.SnapshotDurations();
        var errors = telemetry.SnapshotErrors();

        var get = Assert.Single(activities, a => a.OperationName == S3Client.ActivityNames.GetObject);
        var use = Assert.Single(activities, a => a.OperationName == S3Client.ActivityNames.GetObjectUse);
        Assert.Equal(ActivityStatusCode.Ok, get.Status);
        Assert.Equal(ActivityStatusCode.Ok, use.Status);

        var operationId = Assert.IsType<string>(get.GetTagItem("s3.operation_id"));
        Assert.Equal(operationId, use.GetTagItem("s3.operation_id"));
        Assert.Equal(5L, use.GetTagItem("s3.body.size"));

        Assert.Contains(
            durations,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.GetObject)
                && !HasTag(m.Tags, S3Metrics.ErrorTypeTagName));
        Assert.Contains(
            durations,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.GetObjectUse)
                && !HasTag(m.Tags, S3Metrics.ErrorTypeTagName));
        Assert.DoesNotContain(
            errors,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.GetObject)
                || TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.GetObjectUse));
    }

    [Fact]
    public async Task GetObjectOrNullAsync_not_found_records_success_metric_not_error()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        using var telemetry = new TelemetryCapture();
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        Assert.Null(await client.GetObjectOrNullAsync("missing.txt"));

        var activities = telemetry.SnapshotActivities();
        var durations = telemetry.SnapshotDurations();
        var errors = telemetry.SnapshotErrors();

        var activity = Assert.Single(activities, a => a.OperationName == S3Client.ActivityNames.GetObject);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);

        Assert.Contains(
            durations,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.GetObject)
                && !HasTag(m.Tags, S3Metrics.ErrorTypeTagName));
        Assert.DoesNotContain(
            errors,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.GetObject));
    }

    [Fact]
    public async Task GetObjectAsync_not_found_records_not_found_error_metric()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        using var telemetry = new TelemetryCapture();
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await Assert.ThrowsAsync<AmazonS3Exception>(() => client.GetObjectAsync("missing.txt"));

        var activities = telemetry.SnapshotActivities();
        var errors = telemetry.SnapshotErrors();

        var activity = Assert.Single(activities, a => a.OperationName == S3Client.ActivityNames.GetObject);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("Amazon.S3.AmazonS3Exception", activity.GetTagItem("exception.type"));
        Assert.Equal("NoSuchKey", activity.GetTagItem("aws.s3.error_code"));
        Assert.Equal(404, activity.GetTagItem("http.response.status_code"));

        Assert.Contains(
            errors,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.GetObject)
                && TagEquals(m.Tags, S3Metrics.ErrorTypeTagName, S3Metrics.NotFoundErrorType));
    }

    [Fact]
    public async Task ObjectExistsAsync_not_found_records_success_metric()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey",
            });

        using var telemetry = new TelemetryCapture();
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        Assert.False(await client.ObjectExistsAsync("missing.txt"));

        var durations = telemetry.SnapshotDurations();
        var errors = telemetry.SnapshotErrors();

        Assert.Contains(
            durations,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.ObjectExists)
                && !HasTag(m.Tags, S3Metrics.ErrorTypeTagName));
        Assert.DoesNotContain(
            errors,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.ObjectExists));
    }

    [Fact]
    public async Task CopyObjectAsync_tags_relative_copy_source()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopyObjectResponse());

        using var telemetry = new TelemetryCapture();
        using var client = new S3Client(PrefixOptions(), amazon.Object);

        await client.CopyObjectAsync("from.txt", "to.txt");

        var activity = Assert.Single(
            telemetry.SnapshotActivities(),
            a => a.OperationName == S3Client.ActivityNames.CopyObject);
        Assert.Equal("to.txt", activity.GetTagItem("aws.s3.key"));
        Assert.Equal("from.txt", activity.GetTagItem("aws.s3.copy_source"));
    }

    [Fact]
    public async Task Null_logger_still_records_activities_and_metrics()
    {
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        using var telemetry = new TelemetryCapture();
        using var client = new S3Client(PrefixOptions(), amazon.Object, logger: null);

        await client.DeleteObjectAsync("notes/gone.txt");

        var activities = telemetry.SnapshotActivities();
        var durations = telemetry.SnapshotDurations();

        Assert.Contains(activities, a => a.OperationName == S3Client.ActivityNames.DeleteObject);
        Assert.Contains(
            durations,
            m => TagEquals(m.Tags, S3Metrics.OperationTagName, S3Client.ActivityNames.DeleteObject));
    }

    private static S3ClientOptions PrefixOptions() => new()
    {
        BucketName = "bucket",
        KeyPrefix = "qa1",
    };

    private static bool TagEquals(IEnumerable<KeyValuePair<string, object?>> tags, string name, object? expected)
        => tags.Any(t => t.Key == name && Equals(t.Value, expected));

    private static bool HasTag(IEnumerable<KeyValuePair<string, object?>> tags, string name)
        => tags.Any(t => t.Key == name);

    private sealed class TelemetryCapture : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;
        private readonly object _gate = new();

        public TelemetryCapture()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == S3Client.TelemetrySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_gate)
                    {
                        StoppedActivities.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener();
            _meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == S3Client.TelemetrySourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            {
                lock (_gate)
                {
                    Durations.Add((instrument.Name, measurement, tags.ToArray()));
                }
            });
            _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                lock (_gate)
                {
                    Errors.Add((instrument.Name, measurement, tags.ToArray()));
                }
            });
            _meterListener.Start();
        }

        public List<Activity> StoppedActivities { get; } = new();

        public List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> Durations { get; } = new();

        public List<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> Errors { get; } = new();

        public List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> SnapshotDurations()
        {
            lock (_gate)
            {
                return Durations.ToList();
            }
        }

        public List<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> SnapshotErrors()
        {
            lock (_gate)
            {
                return Errors.ToList();
            }
        }

        public List<Activity> SnapshotActivities()
        {
            lock (_gate)
            {
                return StoppedActivities.ToList();
            }
        }

        public void Dispose()
        {
            _activityListener.Dispose();
            _meterListener.Dispose();
        }
    }
}
