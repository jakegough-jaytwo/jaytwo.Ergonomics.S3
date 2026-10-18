using System;
using System.Globalization;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// The <c>Content-Range</c> header from a partial get (<c>bytes start-end/size</c>).
/// <see cref="Start"/> and <see cref="End"/> are inclusive. <see cref="ObjectSize"/> is
/// <c>null</c> when the total is <c>*</c>.
/// </summary>
public readonly struct S3ContentRange : IEquatable<S3ContentRange>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="S3ContentRange"/> struct.
    /// </summary>
    /// <param name="start">Inclusive starting byte. Must be at least 0.</param>
    /// <param name="end">Inclusive ending byte. Must be at least <paramref name="start"/>.</param>
    /// <param name="objectSize">Full object size, when the header includes one.</param>
    public S3ContentRange(long start, long end, long? objectSize = null)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start must be at least 0.");
        }

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "End must be at least start.");
        }

        // Length is end - start + 1. The only overflow is start 0 and end long.MaxValue.
        if (start == 0 && end == long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "Range length exceeds the maximum byte count.");
        }

        if (objectSize is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(objectSize), objectSize, "Object size must be at least 0.");
        }

        // Complete length must be past the inclusive end (bytes 0-9/10). A total of * is unknown, not invalid.
        if (objectSize is long size && size <= end)
        {
            throw new ArgumentOutOfRangeException(
                nameof(objectSize),
                objectSize,
                "Object size must be greater than end.");
        }

        Start = start;
        End = end;
        ObjectSize = objectSize;
    }

    public long Start { get; }

    public long End { get; }

    /// <summary>Full object size from the header. <c>null</c> when the total is <c>*</c>.</summary>
    public long? ObjectSize { get; }

    /// <summary>Number of bytes in this range, <c>End - Start + 1</c>.</summary>
    public long Length => End - Start + 1;

    public static bool operator ==(S3ContentRange left, S3ContentRange right) => left.Equals(right);

    public static bool operator !=(S3ContentRange left, S3ContentRange right) => !left.Equals(right);

    /// <summary>
    /// The <c>Content-Range</c> value, such as <c>bytes 0-1023/5000</c> or <c>bytes 0-9/*</c>.
    /// </summary>
    public override string ToString()
    {
        var size = ObjectSize?.ToString(CultureInfo.InvariantCulture) ?? "*";
        return FormattableString.Invariant($"bytes {Start}-{End}/{size}");
    }

    public bool Equals(S3ContentRange other)
        => Start == other.Start && End == other.End && ObjectSize == other.ObjectSize;

    public override bool Equals(object? obj) => obj is S3ContentRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, End, ObjectSize);

    /// <summary>
    /// Parses <c>bytes start-end/size</c>. Returns <c>null</c> when <paramref name="header"/>
    /// is missing or not that shape. A bad header does not fail the get.
    /// </summary>
    internal static S3ContentRange? TryParse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var value = header.Trim();
        const string prefix = "bytes ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var range = value.Substring(prefix.Length);
        var dash = range.IndexOf('-');
        var slash = range.IndexOf('/');
        if (dash <= 0 || slash <= dash + 1 || slash == range.Length - 1)
        {
            return null;
        }

        if (!long.TryParse(range.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            || !long.TryParse(range.Substring(dash + 1, slash - dash - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var end)
            || end < start)
        {
            return null;
        }

        var sizeText = range.Substring(slash + 1);
        long? objectSize;
        if (sizeText == "*")
        {
            objectSize = null;
        }
        else if (long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size))
        {
            objectSize = size;
        }
        else
        {
            return null;
        }

        // A bad header does not fail the get. Skip values the constructor would reject.
        if (start == 0 && end == long.MaxValue)
        {
            return null;
        }

        if (objectSize is long completeLength && completeLength <= end)
        {
            return null;
        }

        return new S3ContentRange(start, end, objectSize);
    }
}
