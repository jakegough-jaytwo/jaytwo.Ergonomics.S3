using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Xunit;

namespace jaytwo.Ergonomics.S3.Tests.HappyPath;

[Trait("Category", "Minio")]
public class S3ClientRoundTripTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public S3ClientRoundTripTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BucketExists()
    {
        await using var scope = await CreateReadyScopeAsync();

        Assert.True(await scope.Client.BucketExistsAsync());
    }

    [Fact]
    public async Task Put_get_delete_bytes_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/hello.txt";
        var bytes = Encoding.UTF8.GetBytes("hello from minio");

        await client.PutObjectAsync(key, bytes);

        Assert.True(await client.ObjectExistsAsync(key));

        using (var response = await client.GetObjectAsync(key))
        using (var reader = new StreamReader(response.Body!, Encoding.UTF8))
        {
            Assert.Equal("hello from minio", await reader.ReadToEndAsync());
        }

        await client.DeleteObjectAsync(key);
        Assert.False(await client.ObjectExistsAsync(key));
    }

    [Fact]
    public async Task Put_bytes_with_content_type_and_head_metadata()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/bytes.bin";
        var bytes = Encoding.UTF8.GetBytes("payload");

        await client.PutObjectAsync(key, bytes, put => put.ContentType = "text/plain");

        var metadata = await client.HeadObjectAsync(key);

        Assert.Equal("text/plain", metadata.ContentType);
        Assert.Equal(bytes.Length, metadata.Size);
    }

    [Fact]
    public async Task Put_stream_with_content_length_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-length.bin";
        var bytes = Encoding.UTF8.GetBytes("stream with length");

        using (var stream = new MemoryStream(bytes))
        {
            await client.PutObjectAsync(key, stream, contentLength: bytes.Length);
        }

        Assert.Equal(bytes, await client.GetObjectBytesAsync(key));
        Assert.Equal(bytes.Length, (await client.HeadObjectAsync(key)).Size);
    }

    [Fact]
    public async Task Put_stream_with_content_length_and_configure_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-length-configure.bin";
        var bytes = Encoding.UTF8.GetBytes("stream with length and configure");
        var md5 = ComputeMd5(bytes);

        using (var stream = new MemoryStream(bytes))
        {
            await client.PutObjectAsync(
                key,
                stream,
                contentLength: bytes.Length,
                request => request.MD5Digest = Convert.ToBase64String(md5));
        }

        Assert.Equal(bytes, await client.GetObjectBytesAsync(key));
        Assert.Equal(bytes.Length, (await client.HeadObjectAsync(key)).Size);
    }

    [Fact]
    public async Task Put_stream_with_null_content_length_and_configure_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-null-length-configure.bin";
        var bytes = Encoding.UTF8.GetBytes("stream with null length and configure");
        var md5 = ComputeMd5(bytes);

        using (var stream = new MemoryStream(bytes))
        {
            await client.PutObjectAsync(
                key,
                stream,
                contentLength: null,
                request => request.MD5Digest = Convert.ToBase64String(md5));
        }

        Assert.Equal(bytes, await client.GetObjectBytesAsync(key));
    }

    [Fact]
    public async Task Put_stream_with_wrong_md5_via_configure_is_rejected()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-bad-md5.bin";
        var bytes = Encoding.UTF8.GetBytes("stream with bad md5");
        var wrongMd5 = ComputeMd5(Encoding.UTF8.GetBytes("different payload"));

        using var stream = new MemoryStream(bytes);
        var ex = await Assert.ThrowsAsync<AmazonS3Exception>(
            () => client.PutObjectAsync(
                key,
                stream,
                contentLength: bytes.Length,
                request => request.MD5Digest = Convert.ToBase64String(wrongMd5)));

        Assert.Contains("Digest", ex.ErrorCode ?? ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await client.ObjectExistsAsync(key));
    }

    [Fact]
    public async Task Put_stream_with_content_type_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-content-type.bin";
        var bytes = Encoding.UTF8.GetBytes("content type stream");

        using (var stream = new MemoryStream(bytes))
        {
            await client.PutObjectAsync(key, stream, "application/octet-stream");
        }

        var metadata = await client.HeadObjectAsync(key);
        Assert.Equal("application/octet-stream", metadata.ContentType);
        Assert.Equal(bytes, await client.GetObjectBytesAsync(key));
    }

    [Fact]
    public async Task Put_stream_with_configure_content_type_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-configured.bin";
        var bytes = Encoding.UTF8.GetBytes("configured stream");

        using (var stream = new MemoryStream(bytes))
        {
            await client.PutObjectAsync(key, stream, put => put.ContentType = "application/octet-stream");
        }

        var metadata = await client.HeadObjectAsync(key);
        Assert.Equal("application/octet-stream", metadata.ContentType);
        Assert.Equal(bytes, await client.GetObjectBytesAsync(key));
    }

    [Fact]
    public async Task Put_bytes_with_content_type_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/bytes-content-type.txt";
        var bytes = Encoding.UTF8.GetBytes("hello typed");

        await client.PutObjectAsync(key, bytes, "text/plain");

        var metadata = await client.HeadObjectAsync(key);
        Assert.Equal("text/plain", metadata.ContentType);
        Assert.Equal("hello typed", await client.GetObjectStringAsync(key));
    }

    [Fact]
    public async Task Put_get_string_helpers_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/string-helpers.txt";
        const string content = "hello helpers";
        var bytes = Encoding.UTF8.GetBytes(content);

        await client.PutObjectAsync(key, bytes);

        Assert.Equal(content, await client.GetObjectStringAsync(key));
        Assert.Equal(bytes, await client.GetObjectBytesAsync(key));
    }

    [Fact]
    public async Task GetObjectOrNull_and_bytes_or_null_return_null_for_missing_key()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        Assert.Null(await client.GetObjectOrNullAsync("missing-or-null.txt"));
        Assert.Null(await client.GetObjectBytesOrNullAsync("missing-or-null.txt"));
        Assert.Null(await client.GetObjectStringOrNullAsync("missing-or-null.txt"));
    }

    [Fact]
    public async Task CopyObject_prefixes_source_and_destination()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string source = "notes/copy-source.txt";
        const string destination = "notes/copy-dest.txt";
        var bytes = Encoding.UTF8.GetBytes("copy me");

        await client.PutObjectAsync(source, bytes);
        await client.CopyObjectAsync(source, destination);

        Assert.Equal(bytes, await client.GetObjectBytesAsync(destination));
        Assert.True(await client.ObjectExistsAsync(source));
    }

    [Fact]
    public async Task KeyPrefix_is_applied_on_write_and_stripped_on_list()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        await client.PutObjectAsync("notes/a.txt", Encoding.UTF8.GetBytes("a"));
        await client.PutObjectAsync("notes/b.txt", Encoding.UTF8.GetBytes("bb"));

        var page = await client.ListObjectsAsync("notes/");
        var byKey = page.Objects.OrderBy(x => x.Key).ToList();

        Assert.Equal(new[] { "notes/a.txt", "notes/b.txt" }, byKey.Select(x => x.Key));
        Assert.Equal(1, byKey[0].Size);
        Assert.Equal(2, byKey[1].Size);
        Assert.All(byKey, item => Assert.NotNull(item.LastModified));
        Assert.All(byKey, item => Assert.DoesNotContain(client.KeyPrefix.TrimEnd('/'), item.Key));
    }

    [Fact]
    public async Task GetObjectAsync_returns_headers_and_body_together()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/http-browser.bin";
        var bytes = Encoding.UTF8.GetBytes("payload for headers");

        await client.PutObjectAsync(key, bytes, put => put.ContentType = "text/plain");

        using var file = await client.GetObjectAsync(key);

        Assert.Equal(key, file.Key);
        Assert.Equal(bytes.Length, file.Size);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal("text/plain", file.Headers["Content-Type"]);
        Assert.False(string.IsNullOrEmpty(file.ETag));
        Assert.NotNull(file.Body);
        Assert.Equal(bytes, await ReadAllAsync(file.Body!));
    }

    [Fact]
    public async Task HeadObjectAsync_returns_same_shape_with_null_body()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/head-only.txt";
        await client.PutObjectAsync(key, Encoding.UTF8.GetBytes("head"), put => put.ContentType = "text/plain");

        using var head = await client.HeadObjectAsync(key);

        Assert.Equal(key, head.Key);
        Assert.Equal(4, head.Size);
        Assert.Equal("text/plain", head.ContentType);
        Assert.Null(head.Body);
    }

    [Fact]
    public async Task ListObjects_paginates_with_maxKeys_and_continuation_token()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        var keys = new[] { "page/1.txt", "page/2.txt", "page/3.txt" };
        foreach (var key in keys)
        {
            await client.PutObjectAsync(key, Encoding.UTF8.GetBytes(key));
        }

        var first = await client.ListObjectsAsync("page/", maxKeys: 2);
        Assert.Equal(2, first.Objects.Count);
        Assert.True(first.IsTruncated);
        Assert.False(string.IsNullOrEmpty(first.NextContinuationToken));

        var second = await client.ListObjectsAsync(
            "page/",
            maxKeys: 2,
            continuationToken: first.NextContinuationToken);
        Assert.Single(second.Objects);
        Assert.False(second.IsTruncated);

        var all = new List<string>();
        await foreach (var item in client.ListAllObjectsAsync("page/"))
        {
            all.Add(item.Key);
        }

        Assert.Equal(keys.OrderBy(x => x), all.OrderBy(x => x));
    }

    [Fact]
    public async Task ListObjects_and_ListAllObjects_honor_startAfter()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        var keys = new[] { "start/a.txt", "start/b.txt", "start/c.txt" };
        foreach (var key in keys)
        {
            await client.PutObjectAsync(key, Encoding.UTF8.GetBytes(key));
        }

        var page = await client.ListObjectsAsync("start/", startAfter: "start/a.txt");
        Assert.Equal(new[] { "start/b.txt", "start/c.txt" }, page.Objects.Select(x => x.Key).OrderBy(x => x));

        var all = new List<string>();
        await foreach (var item in client.ListAllObjectsAsync("start/", startAfter: "start/a.txt"))
        {
            all.Add(item.Key);
        }

        Assert.Equal(new[] { "start/b.txt", "start/c.txt" }, all.OrderBy(x => x));
    }

    [Fact]
    public async Task ObjectExists_returns_false_for_missing_key_via_IsNotFound()
    {
        await using var scope = await CreateReadyScopeAsync();

        Assert.False(await scope.Client.ObjectExistsAsync("does-not-exist.txt"));
    }

    [Fact]
    public async Task PutObjectMultipart_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/multipart.bin";
        var partLength = S3Client.MinimumMultipartPartLength;
        var bytes = new byte[partLength + 64];
        new Random(42).NextBytes(bytes);

        using (var stream = new MemoryStream(bytes))
        {
            var put = await client.PutObjectMultipartAsync(key, stream, partLength, "application/octet-stream");
            Assert.True(put.IsMultipart);
            Assert.Equal(2, put.PartCount);
            Assert.Equal(bytes.Length, put.ContentLength);
        }

        Assert.Equal(bytes, await client.GetObjectBytesAsync(key));
        var head = await client.HeadObjectAsync(key);
        Assert.Equal(bytes.Length, head.Size);
        Assert.Equal("application/octet-stream", head.ContentType);
    }

    private static byte[] ComputeMd5(byte[] bytes)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(bytes);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private async Task<MinioTestScope> CreateReadyScopeAsync()
    {
        Assert.True(_fixture.Minio.UseMinioServer, "UseMinioServer requires MinIO. From the minio directory, run `make`.");

        var scope = _fixture.Minio.CreateScope();
        // Test harness only. The library does not create buckets.
        if (!await scope.Client.BucketExistsAsync())
        {
            await scope.Client.AmazonS3.PutBucketAsync(new PutBucketRequest
            {
                BucketName = scope.Client.BucketName,
            });
        }

        return scope;
    }
}
