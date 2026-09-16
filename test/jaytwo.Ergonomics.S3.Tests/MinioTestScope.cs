using System;
using System.Threading.Tasks;

namespace jaytwo.Ergonomics.S3.Tests;

/// <summary>
/// Owns a MinIO-backed <see cref="S3Client"/> and deletes every object under its
/// <see cref="S3Client.KeyPrefix"/> when disposed, so failed or incomplete tests
/// do not leave orphans in the shared bucket.
/// </summary>
public sealed class MinioTestScope : IAsyncDisposable
{
    public MinioTestScope(S3Client client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public S3Client Client { get; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await foreach (var obj in Client.ListAllObjectsAsync())
            {
                await Client.DeleteObjectAsync(obj.Key).ConfigureAwait(false);
            }
        }
        finally
        {
            Client.Dispose();
        }
    }
}
