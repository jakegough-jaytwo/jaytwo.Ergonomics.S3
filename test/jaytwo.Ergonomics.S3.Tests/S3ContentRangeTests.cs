using System;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class S3ContentRangeTests
{
    [Fact]
    public void TryParse_reads_a_closed_content_range()
    {
        // arrange (no setup needed)

        // act
        var range = S3ContentRange.TryParse("bytes 0-1023/5000");

        // assert
        Assert.Equal(0, range!.Value.Start);
        Assert.Equal(1023, range.Value.End);
        Assert.Equal(1024, range.Value.Length);
        Assert.Equal(5000, range.Value.ObjectSize);
        Assert.Equal("bytes 0-1023/5000", range.Value.ToString());
    }

    [Fact]
    public void TryParse_reads_a_case_insensitive_unit()
    {
        // arrange (no setup needed)

        // act
        var range = S3ContentRange.TryParse("  BYTES 1-2/3  ");

        // assert
        Assert.Equal(1, range!.Value.Start);
        Assert.Equal(2, range.Value.End);
        Assert.Equal(3, range.Value.ObjectSize);
    }

    [Fact]
    public void TryParse_leaves_object_size_null_when_total_is_unknown()
    {
        // arrange (no setup needed)

        // act
        var range = S3ContentRange.TryParse("bytes 0-9/*");

        // assert
        Assert.Equal(0, range!.Value.Start);
        Assert.Equal(9, range.Value.End);
        Assert.Null(range.Value.ObjectSize);
        Assert.Equal("bytes 0-9/*", range.Value.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    [InlineData("bytes 5-4/10")]
    [InlineData("bytes 0-1023/5000 extra")]
    [InlineData("bytes=0-1023/5000")]
    [InlineData("bytes 0-1/1")]
    [InlineData("bytes 0-1/0")]
    [InlineData("bytes */10")]
    [InlineData("bytes 0-9223372036854775807/*")]
    public void TryParse_returns_null_when_the_header_is_not_a_byte_range(string? header)
    {
        // arrange (no setup needed)

        // act
        var range = S3ContentRange.TryParse(header);

        // assert
        Assert.Null(range);
    }

    [Fact]
    public void Constructor_rejects_an_end_before_start()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new S3ContentRange(5, 4));

        // assert
        Assert.Equal("end", ex.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_negative_start()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new S3ContentRange(-1, 0));

        // assert
        Assert.Equal("start", ex.ParamName);
    }

    [Fact]
    public void Constructor_rejects_an_object_size_that_does_not_cover_the_end()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new S3ContentRange(0, 9, objectSize: 9));

        // assert
        Assert.Equal("objectSize", ex.ParamName);
    }

    [Fact]
    public void Constructor_rejects_a_length_that_does_not_fit()
    {
        // arrange (no setup needed)

        // act
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new S3ContentRange(0, long.MaxValue));

        // assert
        Assert.Equal("end", ex.ParamName);
    }

    [Fact]
    public void TryParse_reads_a_range_that_ends_at_max_value()
    {
        // arrange (no setup needed)

        // act
        var range = S3ContentRange.TryParse("bytes 1-9223372036854775807/*");

        // assert
        Assert.Equal(1, range!.Value.Start);
        Assert.Equal(long.MaxValue, range.Value.End);
        Assert.Equal(long.MaxValue, range.Value.Length);
        Assert.Null(range.Value.ObjectSize);
    }
}
