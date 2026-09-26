using System;
using Amazon.S3;
using jaytwo.Ergonomics.Logging;
using Microsoft.Extensions.Logging;

namespace jaytwo.Ergonomics.S3.Logging;

public class S3ObjectEventLogger : EventLogger
{
    public S3ObjectEventLogger(ILogger? logger, string? operationId = null)
        : base(logger)
    {
        OperationId = operationId;
    }

    /// <summary>
    /// Present only for multi-event operations (get headers + stream use, ListAll pages).
    /// Single-event calls omit it.
    /// </summary>
    public string? OperationId { get; }

    public void LogObjectPut(string bucket, string? keyPrefix, string key, TimeSpan elapsedTime, long? contentLength = null, string? etag = null, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectPut,
                "Put object in {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.PutObject),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("content_length", contentLength),
                ("etag", etag),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogObjectMultipartPut(
        string bucket,
        string? keyPrefix,
        string key,
        TimeSpan elapsedTime,
        long contentLength,
        int partCount,
        string? etag = null,
        Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectMultipartPut,
                "Put object with multipart upload in {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.PutObjectMultipart),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("content_length", contentLength),
                ("part_count", partCount),
                ("etag", etag),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogObjectGet(string bucket, string? keyPrefix, string key, TimeSpan elapsedTime, long? contentLength = null, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectGet,
                "Got object headers in {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.GetObject),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("content_length", contentLength),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogObjectStreamClosed(
        string bucket,
        string? keyPrefix,
        string key,
        TimeSpan elapsedTime,
        long bytesRead,
        Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectStreamClosed,
                "Closed object stream after {elapsed_time} ({bytes_read} bytes read).",
                FormatTimePretty(elapsedTime),
                bytesRead)
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.GetObjectUse),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("bytes_read", bytesRead),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogObjectHead(string bucket, string? keyPrefix, string key, TimeSpan elapsedTime, long? contentLength = null, string? etag = null, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectHead,
                "Headed object in {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.HeadObject),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("content_length", contentLength),
                ("etag", etag),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogObjectDeleted(string bucket, string? keyPrefix, string key, TimeSpan elapsedTime, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectDeleted,
                "Deleted object in {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.DeleteObject),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogObjectCopied(
        string bucket,
        string? keyPrefix,
        string sourceKey,
        string destinationKey,
        TimeSpan elapsedTime,
        string? etag = null,
        Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectCopied,
                "Copied object in {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.CopyObject),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("source_key", sourceKey),
                ("key", destinationKey),
                ("etag", etag),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    /// <summary>
    /// Expected miss (GetOrNull / ObjectExists false). Debug, not <see cref="LogOperationFailed"/>.
    /// </summary>
    public void LogObjectNotFound(
        string s3Operation,
        string bucket,
        string? keyPrefix,
        string key,
        TimeSpan elapsedTime,
        Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectNotFound,
                "Object not found (looked up in {elapsed_time}).",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", s3Operation),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogObjectListed(string bucket, string? keyPrefix, string? listPrefix, TimeSpan elapsedTime, int keyCount, bool isTruncated, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectListed,
                "Listed {list_key_count} object{plural} in {elapsed_time}.",
                keyCount,
                keyCount == 1 ? string.Empty : "s",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.ListObjects),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("list_prefix", listPrefix),
                ("list_key_count", keyCount),
                ("is_truncated", isTruncated),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogObjectExists(string bucket, string? keyPrefix, string key, TimeSpan elapsedTime, bool exists, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        if (exists)
        {
            BuildMessage(
                    EventIds.ObjectEvents.ObjectExists,
                    "Object exists check completed in {elapsed_time}.",
                    FormatTimePretty(elapsedTime))
                .WithFields(
                    ("s3_operation", S3Client.ActivityNames.ObjectExists),
                    ("bucket", bucket),
                    ("key_prefix", keyPrefix),
                    ("key", key),
                    ("object_exists", true),
                    ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
                .WithExtraConfig(AddOperationIdIfExists)
                .WithExtraConfig(extraConfig)
                .Debug();
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.ObjectNotFound,
                "Object does not exist (checked in {elapsed_time}).",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.ObjectExists),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("object_exists", false),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogBucketExists(string bucket, TimeSpan elapsedTime, bool exists, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Debug))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.BucketExists,
                "Bucket exists check completed in {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", S3Client.ActivityNames.BucketExists),
                ("bucket", bucket),
                ("bucket_exists", exists),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(extraConfig)
            .Debug();
    }

    public void LogOperationFailed(string s3Operation, string bucket, string? keyPrefix, string? key, TimeSpan elapsedTime, Exception ex, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Error))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.OperationFailed,
                "S3 operation failed after {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", s3Operation),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithExtraConfig(AddAmazonS3ExceptionFields)
            .WithException(ex)
            .WithExtraConfig(extraConfig)
            .Error();

        void AddAmazonS3ExceptionFields(LogBuilder builder)
        {
            if (ex is AmazonS3Exception s3Ex)
            {
                builder.WithFields(
                    ("http_status_code", (int)s3Ex.StatusCode),
                    ("error_code", s3Ex.ErrorCode));
            }
        }
    }

    // Cancellation is the caller's intent, not a fault, so this stays below Error.
    public void LogCancelled(string s3Operation, string bucket, string? keyPrefix, string? key, TimeSpan elapsedTime, Exception ex, Action<LogBuilder>? extraConfig = null)
    {
        if (!IsEnabled(LogLevel.Warning))
        {
            return;
        }

        BuildMessage(
                EventIds.ObjectEvents.Cancelled,
                "S3 operation cancelled after {elapsed_time}.",
                FormatTimePretty(elapsedTime))
            .WithFields(
                ("s3_operation", s3Operation),
                ("bucket", bucket),
                ("key_prefix", keyPrefix),
                ("key", key),
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(AddOperationIdIfExists)
            .WithException(ex)
            .WithExtraConfig(extraConfig)
            .Warning();
    }

    private void AddOperationIdIfExists(LogBuilder builder)
    {
        if (OperationId is not null)
        {
            builder.WithFields(("operation_id", OperationId));
        }
    }
}
