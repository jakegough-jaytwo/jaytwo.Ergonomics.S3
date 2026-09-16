using System;
using jaytwo.Ergonomics.S3;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class S3KeyTests
{
    [Fact]
    public void NormalizePrefix_returns_empty_for_null_or_empty()
    {
        Assert.Equal(string.Empty, S3Key.NormalizePrefix(null));
        Assert.Equal(string.Empty, S3Key.NormalizePrefix(string.Empty));
    }

    [Fact]
    public void NormalizePrefix_appends_trailing_slash()
    {
        Assert.Equal("app/", S3Key.NormalizePrefix("app"));
        Assert.Equal("app/", S3Key.NormalizePrefix("app/"));
        Assert.Equal("app/", S3Key.NormalizePrefix("app///"));
    }

    [Fact]
    public void Combine_joins_prefix_and_key()
    {
        Assert.Equal("app/notes/a.txt", S3Key.Combine("app", "notes/a.txt"));
        Assert.Equal("notes/a.txt", S3Key.Combine(null, "notes/a.txt"));
        Assert.Equal("app/", S3Key.Combine("app", null));
        Assert.Equal(string.Empty, S3Key.Combine(null, null));
    }

    [Fact]
    public void StripPrefix_removes_normalized_prefix()
    {
        Assert.Equal("notes/a.txt", S3Key.StripPrefix("app/notes/a.txt", "app"));
        Assert.Equal("notes/a.txt", S3Key.StripPrefix("notes/a.txt", null));
    }

    [Fact]
    public void StripPrefix_throws_when_key_escapes_prefix()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => S3Key.StripPrefix("other/a.txt", "app"));
        Assert.Equal("S3 key escaped configured KeyPrefix.", ex.Message);
    }
}
