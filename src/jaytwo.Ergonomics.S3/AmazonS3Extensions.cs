using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

public static class AmazonS3Extensions
{
    public static Task<PutObjectResponse> PutObjectAsync(
        this IAmazonS3 client,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(client, nameof(client));
        Guard.NotNull(configureRequest, nameof(configureRequest));

        var request = new PutObjectRequest
        {
            AutoCloseStream = false,
        };
        configureRequest(request);
        return client.PutObjectAsync(request, cancellationToken);
    }

    public static Task<GetObjectResponse> GetObjectAsync(
        this IAmazonS3 client,
        Action<GetObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(client, nameof(client));
        Guard.NotNull(configureRequest, nameof(configureRequest));

        var request = new GetObjectRequest();
        configureRequest(request);
        return client.GetObjectAsync(request, cancellationToken);
    }

    public static Task<GetObjectMetadataResponse> HeadObjectAsync(
        this IAmazonS3 client,
        Action<GetObjectMetadataRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(client, nameof(client));
        Guard.NotNull(configureRequest, nameof(configureRequest));

        var request = new GetObjectMetadataRequest();
        configureRequest(request);
        return client.GetObjectMetadataAsync(request, cancellationToken);
    }

    public static Task<DeleteObjectResponse> DeleteObjectAsync(
        this IAmazonS3 client,
        Action<DeleteObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(client, nameof(client));
        Guard.NotNull(configureRequest, nameof(configureRequest));

        var request = new DeleteObjectRequest();
        configureRequest(request);
        return client.DeleteObjectAsync(request, cancellationToken);
    }

    public static Task<ListObjectsV2Response> ListObjectsV2Async(
        this IAmazonS3 client,
        Action<ListObjectsV2Request> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(client, nameof(client));
        Guard.NotNull(configureRequest, nameof(configureRequest));

        var request = new ListObjectsV2Request();
        configureRequest(request);
        return client.ListObjectsV2Async(request, cancellationToken);
    }

    public static async Task<bool> BucketExistsAsync(
        this IAmazonS3 client,
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(client, nameof(client));
        if (string.IsNullOrEmpty(bucketName))
        {
            throw new ArgumentException("Bucket name is required.", nameof(bucketName));
        }

        var response = await client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        var buckets = response.Buckets;
        if (buckets is null)
        {
            return false;
        }

        foreach (var bucket in buckets)
        {
            if (string.Equals(bucket.BucketName, bucketName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<bool> ObjectExistsAsync(
        this IAmazonS3 client,
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await client.HeadObjectAsync(
                request =>
                {
                    request.BucketName = bucketName;
                    request.Key = key;
                },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.IsNotFound())
        {
            return false;
        }
    }

    public static Task<PutObjectResponse> PutObjectAsync(
        this IAmazonS3 client,
        string bucketName,
        string key,
        Stream inputStream,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));

        return client.PutObjectAsync(
            request =>
            {
                request.BucketName = bucketName;
                request.Key = key;
                request.InputStream = inputStream;
            },
            cancellationToken);
    }

    public static Task<PutObjectResponse> PutObjectAsync(
        this IAmazonS3 client,
        string bucketName,
        string key,
        Stream inputStream,
        Action<PutObjectRequest> configureRequest,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(inputStream, nameof(inputStream));
        Guard.NotNull(configureRequest, nameof(configureRequest));

        return client.PutObjectAsync(
            request =>
            {
                request.BucketName = bucketName;
                request.Key = key;
                request.InputStream = inputStream;
                configureRequest(request);
            },
            cancellationToken);
    }
}
