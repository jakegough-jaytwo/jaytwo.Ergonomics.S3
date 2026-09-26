using System;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// Maps SDK <see cref="DateTime"/> values to <see cref="DateTimeOffset"/>.
/// AWSSDK v4 unmarshals S3 timestamps as UTC (<c>AssumeUniversal | AdjustToUniversal</c>);
/// we wrap that as a zero-offset <see cref="DateTimeOffset"/>. Raw <c>Last-Modified</c> is not
/// re-parsed from <c>HeadersCollection</c> (the SDK does not leave it there).
/// </summary>
internal static class S3Timestamps
{
    /// <summary>
    /// Wraps a present SDK timestamp (expected <see cref="DateTimeKind.Utc"/>) as UTC.
    /// </summary>
    public static DateTimeOffset ToDateTimeOffset(DateTime value)
        => new DateTimeOffset(EnsureUtc(value), TimeSpan.Zero);

    /// <summary>
    /// Maps null or <see cref="DateTime"/> default to null; otherwise UTC.
    /// </summary>
    public static DateTimeOffset? ToDateTimeOffsetOrNull(DateTime? value)
    {
        if (value is null || value.Value == default)
        {
            return null;
        }

        return ToDateTimeOffset(value.Value);
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            // Should not appear with AWSSDK v4 S3 unmarshalling; convert if it does.
            DateTimeKind.Local => value.ToUniversalTime(),
            // Wire contract is UTC; do not let Unspecified be treated as local.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
