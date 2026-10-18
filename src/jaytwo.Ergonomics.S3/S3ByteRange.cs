using System;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// A single byte range for <see cref="S3Client.GetObjectAsync(string, S3ByteRange, System.Threading.CancellationToken)"/>.
/// S3 allows one range per GET. The value is applied as <c>GetObjectRequest.ByteRange</c>
/// before the configure delegate.
/// </summary>
public readonly struct S3ByteRange : IEquatable<S3ByteRange>
{
    private readonly RangeKind _kind;
    private readonly long _offset;
    private readonly long _length;

    /// <summary>
    /// Initializes a new instance of the <see cref="S3ByteRange"/> struct
    /// covering <paramref name="length"/> bytes from <paramref name="offset"/>
    /// (<c>bytes=offset-(offset+length-1)</c>).
    /// </summary>
    /// <param name="offset">Zero-based starting byte. Must be at least 0.</param>
    /// <param name="length">Number of bytes. Must be at least 1.</param>
    public S3ByteRange(long offset, long length)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be at least 0.");
        }

        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be at least 1.");
        }

        // Inclusive end is offset + length - 1, so the last valid end is long.MaxValue.
        if (offset > long.MaxValue - (length - 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                "Offset plus length exceeds the maximum byte index.");
        }

        _kind = RangeKind.Closed;
        _offset = offset;
        _length = length;
    }

    private S3ByteRange(RangeKind kind, long offset, long length)
    {
        _kind = kind;
        _offset = offset;
        _length = length;
    }

    private enum RangeKind
    {
        None = 0,
        Closed,
        OpenEnded,
        Suffix,
    }

    /// <summary>
    /// Starting byte for a closed or open-ended range. <c>null</c> for <see cref="Suffix"/>.
    /// </summary>
    public long? Offset => _kind == RangeKind.None || _kind == RangeKind.Suffix ? null : _offset;

    /// <summary>
    /// Number of bytes for a closed or suffix range. <c>null</c> for <see cref="FromOffset"/>.
    /// </summary>
    public long? Length => _kind == RangeKind.None || _kind == RangeKind.OpenEnded ? null : _length;

    public static bool operator ==(S3ByteRange left, S3ByteRange right) => left.Equals(right);

    public static bool operator !=(S3ByteRange left, S3ByteRange right) => !left.Equals(right);

    /// <summary>
    /// Bytes from <paramref name="offset"/> through the end of the object (<c>bytes=offset-</c>).
    /// </summary>
    public static S3ByteRange FromOffset(long offset)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be at least 0.");
        }

        return new S3ByteRange(RangeKind.OpenEnded, offset, length: 0);
    }

    /// <summary>
    /// The last <paramref name="length"/> bytes of the object (<c>bytes=-length</c>).
    /// </summary>
    public static S3ByteRange Suffix(long length)
    {
        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be at least 1.");
        }

        return new S3ByteRange(RangeKind.Suffix, offset: 0, length);
    }

    /// <summary>
    /// The <c>Range</c> header value: <c>bytes=0-1023</c>, <c>bytes=1024-</c>, or <c>bytes=-500</c>.
    /// An uninitialized value formats as empty.
    /// </summary>
    public override string ToString() => FormatHeader() ?? string.Empty;

    public bool Equals(S3ByteRange other)
        => _kind == other._kind && _offset == other._offset && _length == other._length;

    public override bool Equals(object? obj) => obj is S3ByteRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_kind, _offset, _length);

    internal void EnsureSpecified(string paramName)
    {
        if (_kind == RangeKind.None)
        {
            throw new ArgumentException(
                "S3ByteRange must be created with a constructor, FromOffset, or Suffix.",
                paramName);
        }
    }

    internal ByteRange ToByteRange()
    {
        // Closed ranges use Start/End so the SDK fills both properties.
        // Open-ended and suffix ranges have no end or no start, so they go through the header string.
        if (_kind == RangeKind.Closed)
        {
            return new ByteRange(_offset, _offset + (_length - 1));
        }

        var header = FormatHeader();
        if (header is null)
        {
            throw new InvalidOperationException("S3ByteRange must be created with a constructor, FromOffset, or Suffix.");
        }

        return new ByteRange(header);
    }

    private string? FormatHeader()
    {
        return _kind switch
        {
            RangeKind.Closed => FormattableString.Invariant($"bytes={_offset}-{_offset + (_length - 1)}"),
            RangeKind.OpenEnded => FormattableString.Invariant($"bytes={_offset}-"),
            RangeKind.Suffix => FormattableString.Invariant($"bytes=-{_length}"),
            _ => null,
        };
    }
}
