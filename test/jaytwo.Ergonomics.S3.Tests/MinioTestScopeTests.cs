using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests;

public class MinioTestScopeTests
{
    [Fact]
    public async Task DisposeAsync_deletes_all_objects_under_the_key_prefix()
    {
        var deleted = new List<string>();
        var amazon = new Mock<IAmazonS3>(MockBehavior.Strict);
        amazon
            .Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                IsTruncated = false,
                S3Objects = new List<S3Object>
                {
                    new S3Object { Key = "run/a.txt" },
                    new S3Object { Key = "run/b.txt" },
                },
            });
        amazon
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleted.Add(request.Key))
            .ReturnsAsync(new DeleteObjectResponse());
        amazon.Setup(x => x.Dispose());

        var client = new S3Client(
            new S3ClientOptions { BucketName = "bucket", KeyPrefix = "run/" },
            amazon.Object,
            ownsClient: true);

        await using (new MinioTestScope(client))
        {
        }

        Assert.Equal(new[] { "run/a.txt", "run/b.txt" }, deleted);
        amazon.Verify(x => x.Dispose(), Times.Once);
    }
}
