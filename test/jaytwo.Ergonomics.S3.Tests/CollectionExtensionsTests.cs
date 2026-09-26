using Amazon.S3.Model;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class CollectionExtensionsTests
{
    [Fact]
    public void HeadersCollection_ToDictionary_is_case_insensitive()
    {
        var headers = new HeadersCollection
        {
            ["Content-Type"] = "text/plain",
            ["x-amz-meta-trace"] = "abc",
        };

        var dict = headers.ToDictionary();

        Assert.Equal("text/plain", dict["content-type"]);
        Assert.Equal("abc", dict["X-AMZ-META-TRACE"]);
    }

    [Fact]
    public void MetadataCollection_ToDictionary_is_case_insensitive()
    {
        var metadata = new MetadataCollection();
        metadata["TraceId"] = "abc";

        var dict = metadata.ToDictionary();

        Assert.NotEmpty(dict);
        var key = Assert.Single(dict.Keys);
        Assert.Equal("abc", dict[key]);
        Assert.Equal("abc", dict[key.ToUpperInvariant()]);
    }

    [Fact]
    public void ToDictionary_rejects_null()
    {
        HeadersCollection? headers = null;
        Assert.Throws<System.ArgumentNullException>(() => headers!.ToDictionary());

        MetadataCollection? metadata = null;
        Assert.Throws<System.ArgumentNullException>(() => metadata!.ToDictionary());
    }
}
