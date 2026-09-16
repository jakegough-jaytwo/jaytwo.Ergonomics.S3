using System;
using System.Net;
using Amazon.S3;

namespace jaytwo.Ergonomics.S3;

public static class AmazonS3ExceptionExtensions
{
    public static bool IsNotFound(this AmazonS3Exception exception)
    {
        Guard.NotNull(exception, nameof(exception));

        if (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        if (string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exception.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exception.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var message = exception.Message;
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message.Contains("Not Found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("404", StringComparison.OrdinalIgnoreCase);
    }
}
