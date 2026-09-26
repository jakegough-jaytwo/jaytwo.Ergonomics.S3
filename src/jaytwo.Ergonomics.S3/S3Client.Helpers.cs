using System;
using System.Diagnostics;
using System.Threading;
using Amazon.S3;
using jaytwo.Ergonomics.S3.Logging;
using jaytwo.Ergonomics.S3.Tracing;

namespace jaytwo.Ergonomics.S3;

public partial class S3Client
{
    // Hosts subscribe with AddSource(TelemetrySourceName) / AddMeter(TelemetrySourceName).
    public const string TelemetrySourceName = "jaytwo.Ergonomics.S3";

    private static readonly ActivitySource _activitySource = new(TelemetrySourceName);

    private static string NewOperationId() => Guid.NewGuid().ToString("N");

    private static void AddExceptionInfo(Activity? activity, Exception ex)
    {
        if (activity is null)
        {
            return;
        }

        activity.AddTag("exception.type", ex.GetType().FullName);
        activity.AddTag("exception.message", ex.Message);
        activity.AddTag("exception.stack_trace", ex.StackTrace);

        if (ex is AmazonS3Exception s3Ex)
        {
            activity.AddTag("aws.s3.error_code", s3Ex.ErrorCode);
            activity.AddTag("http.response.status_code", (int)s3Ex.StatusCode);
        }
    }

    private static void LogOperationFailure(
        S3ObjectEventLogger? eventLogger,
        string activityName,
        string bucket,
        string? keyPrefix,
        string? key,
        TimeSpan elapsedTime,
        Exception ex,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested && ex is OperationCanceledException)
        {
            eventLogger?.LogCancelled(activityName, bucket, keyPrefix, key, elapsedTime, ex);
            S3Metrics.RecordFailure(activityName, elapsedTime, S3Metrics.CancelledErrorType);
        }
        else
        {
            eventLogger?.LogOperationFailed(activityName, bucket, keyPrefix, key, elapsedTime, ex);
            S3Metrics.RecordFailure(
                activityName,
                elapsedTime,
                ex is AmazonS3Exception s3Ex && s3Ex.IsNotFound()
                    ? S3Metrics.NotFoundErrorType
                    : ex.GetType().FullName);
        }
    }

    private S3ObjectEventLogger? CreateEventLogger(string? operationId = null)
        => _logger is null ? null : new S3ObjectEventLogger(_logger, operationId);

    /// <param name="operationId">
    /// Set only for multi-event operations (get headers + stream use, ListAll pages).
    /// </param>
    private Activity? StartActivity(string activityName, string? relativeKey, string? operationId = null)
    {
        var activity = _activitySource.StartActivity(activityName, ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        if (operationId is not null)
        {
            activity.SetTag("s3.operation_id", operationId);
        }

        activity.SetTag("aws.s3.bucket", BucketName);

        var keyPrefix = KeyPrefix;
        if (!string.IsNullOrEmpty(keyPrefix))
        {
            activity.SetTag("aws.s3.key_prefix", keyPrefix);
        }

        if (relativeKey is not null)
        {
            activity.SetTag("aws.s3.key", relativeKey);
        }

        return activity;
    }

    private string? KeyPrefixOrNull() => string.IsNullOrEmpty(KeyPrefix) ? null : KeyPrefix;

    public static class ActivityNames
    {
        public const string PutObject = "s3.put_object";
        public const string PutObjectMultipart = "s3.put_object_multipart";
        public const string GetObject = "s3.get_object";
        /// <summary>
        /// Hold + drain of <see cref="S3ObjectResponse.Body"/>.
        /// Headers/TTFB stay on <see cref="GetObject"/>.
        /// </summary>
        public const string GetObjectUse = "s3.get_object.use";
        public const string HeadObject = "s3.head_object";
        public const string DeleteObject = "s3.delete_object";
        public const string CopyObject = "s3.copy_object";
        public const string ListObjects = "s3.list_objects";
        public const string ObjectExists = "s3.object_exists";
        public const string BucketExists = "s3.bucket_exists";
    }
}
