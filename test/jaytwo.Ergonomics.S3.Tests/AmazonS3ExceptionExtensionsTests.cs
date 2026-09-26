using System;
using System.Net;
using Amazon.S3;
using jaytwo.Ergonomics.S3;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class AmazonS3ExceptionExtensionsTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, null, true)]
    [InlineData(HttpStatusCode.OK, "NoSuchKey", true)]
    [InlineData(HttpStatusCode.OK, "NoSuchBucket", true)]
    [InlineData(HttpStatusCode.OK, "NotFound", true)]
    [InlineData(HttpStatusCode.Forbidden, "AccessDenied", false)]
    public void IsNotFound_matches_status_and_error_code(HttpStatusCode statusCode, string? errorCode, bool expected)
    {
        var exception = new AmazonS3Exception("boom")
        {
            StatusCode = statusCode,
            ErrorCode = errorCode,
        };

        Assert.Equal(expected, exception.IsNotFound());
    }

    [Fact]
    public void IsNotFound_matches_minio_message()
    {
        var exception = new AmazonS3Exception("The specified key does not exist. Not Found")
        {
            StatusCode = HttpStatusCode.BadRequest,
        };

        Assert.True(exception.IsNotFound());
    }

    [Fact]
    public void IsNotFound_matches_message_containing_404()
    {
        var exception = new AmazonS3Exception("HTTP 404 from upstream")
        {
            StatusCode = HttpStatusCode.BadRequest,
        };

        Assert.True(exception.IsNotFound());
    }

    [Fact]
    public void IsNotFound_rejects_null()
    {
        AmazonS3Exception? exception = null;
        Assert.Throws<ArgumentNullException>(() => exception!.IsNotFound());
    }
}
