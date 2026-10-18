using System;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class S3ByteRangeTests
{
    [Fact]
    public void Constructor_rejects_a_negative_offset()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new S3ByteRange(-1, 1));

        // assert
        Assert.Equal("offset", ex.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_length_below_one()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new S3ByteRange(0, 0));

        // assert
        Assert.Equal("length", ex.ParamName);
    }

    [Fact]
    public void Constructor_rejects_an_end_past_max_value()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new S3ByteRange(long.MaxValue, 2));

        // assert
        Assert.Equal("length", ex.ParamName);
    }

    [Fact]
    public void FromOffset_rejects_a_negative_offset()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => S3ByteRange.FromOffset(-1));

        // assert
        Assert.Equal("offset", ex.ParamName);
    }

    [Fact]
    public void Suffix_rejects_a_length_below_one()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => S3ByteRange.Suffix(0));

        // assert
        Assert.Equal("length", ex.ParamName);
    }

    [Fact]
    public void Constructor_exposes_offset_and_length()
    {
        // arrange (no setup needed)

        // act
        var range = new S3ByteRange(8, 4);

        // assert
        Assert.Equal(8, range.Offset);
        Assert.Equal(4, range.Length);
        Assert.Equal("bytes=8-11", range.ToString());
        Assert.Equal("bytes=8-11", range.ToByteRange().FormattedByteRange);
    }

    [Fact]
    public void FromOffset_has_no_length()
    {
        // arrange (no setup needed)

        // act
        var range = S3ByteRange.FromOffset(8);

        // assert
        Assert.Equal(8, range.Offset);
        Assert.Null(range.Length);
        Assert.Equal("bytes=8-", range.ToString());
        Assert.Equal("bytes=8-", range.ToByteRange().FormattedByteRange);
    }

    [Fact]
    public void Suffix_has_no_offset()
    {
        // arrange (no setup needed)

        // act
        var range = S3ByteRange.Suffix(8);

        // assert
        Assert.Null(range.Offset);
        Assert.Equal(8, range.Length);
        Assert.Equal("bytes=-8", range.ToString());
        Assert.Equal("bytes=-8", range.ToByteRange().FormattedByteRange);
    }

    [Fact]
    public void Constructor_formats_the_first_byte_as_an_inclusive_range()
    {
        // arrange (no setup needed)

        // act
        var range = new S3ByteRange(0, 1);
        var byteRange = range.ToByteRange();

        // assert
        Assert.Equal("bytes=0-0", range.ToString());
        Assert.Equal(0, byteRange.Start);
        Assert.Equal(0, byteRange.End);
        Assert.Equal("bytes=0-0", byteRange.FormattedByteRange);
    }

    [Fact]
    public void Constructor_allows_an_inclusive_end_at_max_value()
    {
        // arrange (no setup needed)

        // act
        var atMax = new S3ByteRange(long.MaxValue, 1);
        var endingAtMax = new S3ByteRange(long.MaxValue - 1, 2);

        // assert
        Assert.Equal(long.MaxValue, atMax.ToByteRange().End);
        Assert.Equal(long.MaxValue, endingAtMax.ToByteRange().End);
        Assert.Equal(FormattableString.Invariant($"bytes={long.MaxValue}-{long.MaxValue}"), atMax.ToString());
    }

    [Fact]
    public void Default_is_empty_and_not_equal_to_a_range_from_offset_zero()
    {
        // arrange (no setup needed)

        // act
        var range = default(S3ByteRange);

        // assert
        Assert.Equal(string.Empty, range.ToString());
        Assert.Null(range.Offset);
        Assert.Null(range.Length);
        Assert.NotEqual(S3ByteRange.FromOffset(0), range);
        Assert.False(range == S3ByteRange.FromOffset(0));
    }

    [Fact]
    public void Suffix_is_not_equal_to_a_closed_range_with_the_same_length()
    {
        // arrange (no setup needed)

        // act
        var suffix = S3ByteRange.Suffix(8);
        var closed = new S3ByteRange(0, 8);

        // assert
        Assert.NotEqual(closed, suffix);
        Assert.True(suffix == S3ByteRange.Suffix(8));
    }

    [Fact]
    public void ToByteRange_rejects_an_uninitialized_range()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<InvalidOperationException>(() => default(S3ByteRange).ToByteRange());

        // assert
        Assert.Contains("FromOffset", ex.Message, StringComparison.Ordinal);
    }
}
