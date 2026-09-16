using Amazon.S3;
using jaytwo.Ergonomics.S3;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class AmazonS3ClientFactoryTests
{
    [Fact]
    public void Create_uses_path_style_and_service_url_for_custom_endpoint()
    {
        var client = AmazonS3ClientFactory.Create(new S3ClientOptions
        {
            ServiceUrl = "http://localhost:9000",
            AccessKeyId = "key",
            SecretAccessKey = "secret",
        });

        using var amazon = Assert.IsType<AmazonS3Client>(client);
        var config = Assert.IsType<AmazonS3Config>(amazon.Config);
        Assert.True(config.ForcePathStyle);
        Assert.Contains("localhost:9000", config.ServiceURL, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal("4", config.SignatureVersion);
        Assert.Equal("us-east-1", config.AuthenticationRegion);
    }

    [Fact]
    public void Create_uses_region_endpoint_when_service_url_is_missing()
    {
        var client = AmazonS3ClientFactory.Create(new S3ClientOptions
        {
            AuthenticationRegion = "us-west-2",
            AccessKeyId = "key",
            SecretAccessKey = "secret",
            ForcePathStyle = false,
        });

        using var amazon = Assert.IsType<AmazonS3Client>(client);
        var config = Assert.IsType<AmazonS3Config>(amazon.Config);
        Assert.False(config.ForcePathStyle);
        Assert.Equal("us-west-2", config.RegionEndpoint.SystemName);
    }
}
