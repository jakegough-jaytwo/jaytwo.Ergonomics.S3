using System;
using Amazon.Runtime;
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
        Assert.Contains("localhost:9000", config.ServiceURL, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("us-east-1", config.AuthenticationRegion);
        Assert.Equal(SigningAlgorithm.HmacSHA256, config.SignatureMethod);
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

    [Fact]
    public void Create_rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() => AmazonS3ClientFactory.Create(null!));
    }

    [Fact]
    public void Create_falls_back_to_us_east_1_when_authentication_region_is_empty()
    {
        var client = AmazonS3ClientFactory.Create(new S3ClientOptions
        {
            ServiceUrl = "http://localhost:9000",
            AuthenticationRegion = string.Empty,
            AccessKeyId = "key",
            SecretAccessKey = "secret",
        });

        using var amazon = Assert.IsType<AmazonS3Client>(client);
        var config = Assert.IsType<AmazonS3Config>(amazon.Config);
        Assert.Equal("us-east-1", config.AuthenticationRegion);
    }

    [Fact]
    public void Create_honors_force_path_style_override_with_service_url()
    {
        var client = AmazonS3ClientFactory.Create(new S3ClientOptions
        {
            ServiceUrl = "http://localhost:9000",
            ForcePathStyle = false,
            AccessKeyId = "key",
            SecretAccessKey = "secret",
        });

        using var amazon = Assert.IsType<AmazonS3Client>(client);
        var config = Assert.IsType<AmazonS3Config>(amazon.Config);
        Assert.False(config.ForcePathStyle);
    }

    [Fact]
    public void Create_without_access_key_uses_default_credential_chain()
    {
        var client = AmazonS3ClientFactory.Create(new S3ClientOptions
        {
            AuthenticationRegion = "us-east-1",
        });

        using var amazon = Assert.IsType<AmazonS3Client>(client);
        Assert.NotNull(amazon.Config);
    }
}
