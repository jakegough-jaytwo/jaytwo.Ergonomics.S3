using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Amazon.S3;
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
    public async Task EnsureBucketExists_then_BucketExists()
    {
        await using var scope = await CreateReadyScopeAsync();

        Assert.True(await scope.Client.BucketExistsAsync());
    }

    [Fact]
    public async Task Put_get_delete_string_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/hello.txt";
        const string content = "hello from minio";

        await client.PutObjectAsync(key, content);

        Assert.True(await client.ObjectExistsAsync(key));

        using (var response = await client.GetObjectAsync(key))
        using (var reader = new StreamReader(response.ResponseStream, Encoding.UTF8))
        {
            Assert.Equal(content, await reader.ReadToEndAsync());
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

        var metadata = await client.GetObjectMetadataAsync(key);

        Assert.Equal("text/plain", metadata.Headers.ContentType);
        Assert.Equal(bytes.Length, metadata.ContentLength);
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

        Assert.Equal(bytes, await ReadAllBytesAsync(client, key));
        Assert.Equal(bytes.Length, (await client.GetObjectMetadataAsync(key)).ContentLength);
    }

    [Fact]
    public async Task Put_stream_with_md5_and_content_length_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-md5-length.bin";
        var bytes = Encoding.UTF8.GetBytes("stream with md5 and length");
        var md5 = ComputeMd5(bytes);

        using (var stream = new MemoryStream(bytes))
        {
            await client.PutObjectAsync(key, stream, contentLength: bytes.Length, md5);
        }

        Assert.Equal(bytes, await ReadAllBytesAsync(client, key));
        Assert.Equal(bytes.Length, (await client.GetObjectMetadataAsync(key)).ContentLength);
    }

    [Fact]
    public async Task Put_stream_with_md5_without_content_length_round_trip()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-md5-only.bin";
        var bytes = Encoding.UTF8.GetBytes("stream with md5 only");
        var md5 = ComputeMd5(bytes);

        using (var stream = new MemoryStream(bytes))
        {
            await client.PutObjectAsync(key, stream, contentLength: null, md5);
        }

        Assert.Equal(bytes, await ReadAllBytesAsync(client, key));
    }

    [Fact]
    public async Task Put_stream_with_wrong_md5_is_rejected()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        const string key = "notes/stream-bad-md5.bin";
        var bytes = Encoding.UTF8.GetBytes("stream with bad md5");
        var wrongMd5 = ComputeMd5(Encoding.UTF8.GetBytes("different payload"));

        using var stream = new MemoryStream(bytes);
        var ex = await Assert.ThrowsAsync<AmazonS3Exception>(
            () => client.PutObjectAsync(key, stream, contentLength: bytes.Length, wrongMd5));

        Assert.Contains("Digest", ex.ErrorCode ?? ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await client.ObjectExistsAsync(key));
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

        var metadata = await client.GetObjectMetadataAsync(key);
        Assert.Equal("application/octet-stream", metadata.Headers.ContentType);
        Assert.Equal(bytes, await ReadAllBytesAsync(client, key));
    }

    [Fact]
    public async Task KeyPrefix_is_applied_on_write_and_stripped_on_list()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        await client.PutObjectAsync("notes/a.txt", "a");
        await client.PutObjectAsync("notes/b.txt", "b");

        var page = await client.ListObjectsAsync("notes/");
        var keys = page.Objects.Select(x => x.Key).OrderBy(x => x).ToList();

        Assert.Equal(new[] { "notes/a.txt", "notes/b.txt" }, keys);
        Assert.All(keys, key => Assert.DoesNotContain(client.KeyPrefix.TrimEnd('/'), key));
    }

    [Fact]
    public async Task ListObjects_paginates_with_maxKeys_and_continuation_token()
    {
        await using var scope = await CreateReadyScopeAsync();
        var client = scope.Client;

        var keys = new[] { "page/1.txt", "page/2.txt", "page/3.txt" };
        foreach (var key in keys)
        {
            await client.PutObjectAsync(key, key);
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
            await client.PutObjectAsync(key, key);
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

    private static byte[] ComputeMd5(byte[] bytes)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(bytes);
    }

    private static async Task<byte[]> ReadAllBytesAsync(S3Client client, string key)
    {
        using var response = await client.GetObjectAsync(key);
        using var memory = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private async Task<MinioTestScope> CreateReadyScopeAsync()
    {
        Assert.True(_fixture.Minio.UseMinioServer, "UseMinioServer requires MinIO. From the minio directory, run `make`.");

        var scope = _fixture.Minio.CreateScope();
        await scope.Client.EnsureBucketExistsAsync();
        return scope;
    }
}
