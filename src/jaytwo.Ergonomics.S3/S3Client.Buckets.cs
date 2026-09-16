using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

public sealed partial class S3Client
{
    public Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default)
        => AmazonS3.BucketExistsAsync(BucketName, cancellationToken);

    public Task<PutBucketResponse> PutBucketAsync(CancellationToken cancellationToken = default)
        => AmazonS3.PutBucketAsync(new PutBucketRequest { BucketName = BucketName }, cancellationToken);

    public async Task EnsureBucketExistsAsync(CancellationToken cancellationToken = default)
    {
        if (!await BucketExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await PutBucketAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
