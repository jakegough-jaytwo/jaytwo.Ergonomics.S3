using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Ergonomics.S3.Logging;
using jaytwo.Ergonomics.S3.Tracing;

namespace jaytwo.Ergonomics.S3;

public partial class S3Client
{
    public async Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default)
    {
        var eventLogger = CreateEventLogger();
        using var activity = StartActivity(ActivityNames.BucketExists, relativeKey: null);

        var actionStopwatch = Stopwatch.StartNew();
        try
        {
            var exists = await AmazonS3.BucketExistsAsync(BucketName, cancellationToken).ConfigureAwait(false);
            actionStopwatch.Stop();

            eventLogger?.LogBucketExists(BucketName, actionStopwatch.Elapsed, exists);
            S3Metrics.RecordSuccess(ActivityNames.BucketExists, actionStopwatch.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return exists;
        }
        catch (Exception ex)
        {
            AddExceptionInfo(activity, ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogOperationFailure(
                eventLogger,
                ActivityNames.BucketExists,
                BucketName,
                keyPrefix: null,
                key: null,
                actionStopwatch.Elapsed,
                ex,
                cancellationToken);
            throw;
        }
    }
}
