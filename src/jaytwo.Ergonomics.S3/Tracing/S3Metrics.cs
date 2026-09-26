using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace jaytwo.Ergonomics.S3.Tracing;

// Metrics are pre-aggregated, so every tag multiplies the time series count.
// Only bounded-cardinality values belong here; operation_id and key stay on the
// log event and the Activity.
internal static class S3Metrics
{
    internal const string OperationTagName = "s3.operation";
    internal const string ErrorTypeTagName = "error.type";

    // Cancellations are OperationCanceledException; OpenTelemetry allows a low-cardinality
    // domain value here instead of the exception type alone.
    internal const string CancelledErrorType = "cancelled";
    internal const string NotFoundErrorType = "not_found";

    private static readonly Meter Meter = new(S3Client.TelemetrySourceName);

    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "s3.client.operation.duration",
        unit: "s",
        description: "Duration of S3 client operations.");

    private static readonly Counter<long> OperationErrors = Meter.CreateCounter<long>(
        "s3.client.operation.errors",
        unit: "{error}",
        description: "Number of failed S3 client operations.");

    internal static void RecordSuccess(string operation, TimeSpan elapsedTime)
        => OperationDuration.Record(elapsedTime.TotalSeconds, OperationTag(operation));

    internal static void RecordFailure(string operation, TimeSpan elapsedTime, Exception ex)
        => RecordFailure(operation, elapsedTime, ex.GetType().FullName);

    internal static void RecordFailure(string operation, TimeSpan elapsedTime, string? errorType)
    {
        var operationTag = OperationTag(operation);
        var errorTypeTag = new KeyValuePair<string, object?>(ErrorTypeTagName, errorType);

        // Failures have durations too; recording both keeps the histogram a complete
        // picture of latency and lets the counter answer "how many, of what type".
        OperationDuration.Record(elapsedTime.TotalSeconds, operationTag, errorTypeTag);
        OperationErrors.Add(1, operationTag, errorTypeTag);
    }

    private static KeyValuePair<string, object?> OperationTag(string operation)
        => new(OperationTagName, operation);
}
