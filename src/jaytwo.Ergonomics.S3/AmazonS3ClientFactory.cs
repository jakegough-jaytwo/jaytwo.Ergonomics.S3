using System;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// Builds an <see cref="IAmazonS3"/> from <see cref="S3ClientOptions"/>.
/// Custom <see cref="S3ClientOptions.ServiceUrl"/> gets path-style;
/// otherwise the regional AWS endpoint is used. Signing is always SigV4 (AWSSDK v4).
/// </summary>
public static class AmazonS3ClientFactory
{
    public static IAmazonS3 Create(S3ClientOptions options)
    {
        Guard.NotNull(options, nameof(options));

        var s3Config = new AmazonS3Config
        {
            SignatureMethod = SigningAlgorithm.HmacSHA256,
        };

        var region = string.IsNullOrEmpty(options.AuthenticationRegion)
            ? "us-east-1"
            : options.AuthenticationRegion;

        if (!string.IsNullOrEmpty(options.ServiceUrl))
        {
            s3Config.ServiceURL = options.ServiceUrl;
            s3Config.ForcePathStyle = options.ForcePathStyle ?? true;
            s3Config.AuthenticationRegion = region;
        }
        else
        {
            s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
            s3Config.ForcePathStyle = options.ForcePathStyle ?? false;
        }

        if (string.IsNullOrEmpty(options.AccessKeyId))
        {
            return new AmazonS3Client(s3Config);
        }

        return new AmazonS3Client(
            options.AccessKeyId,
            options.SecretAccessKey ?? string.Empty,
            s3Config);
    }
}
