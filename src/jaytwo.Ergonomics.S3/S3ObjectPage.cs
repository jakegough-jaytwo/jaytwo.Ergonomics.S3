using System;
using System.Collections.Generic;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// One page of list results. Keys are relative to the client's <see cref="S3Client.KeyPrefix"/>.
/// </summary>
public sealed class S3ObjectPage
{
    internal S3ObjectPage(IReadOnlyList<S3Object>? objects, string? nextContinuationToken)
    {
        Objects = objects ?? Array.Empty<S3Object>();
        NextContinuationToken = nextContinuationToken;
    }

    public IReadOnlyList<S3Object> Objects { get; }

    /// <summary>
    /// Token to pass back to get the next page; null when the listing is complete.
    /// </summary>
    public string? NextContinuationToken { get; }

    public bool IsTruncated => NextContinuationToken is not null;
}
