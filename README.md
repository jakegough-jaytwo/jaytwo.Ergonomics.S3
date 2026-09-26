# jaytwo.Ergonomics.S3

Ergonomic helpers for AWSSDK.S3.

[![NuGet Version](https://img.shields.io/nuget/v/jaytwo.Ergonomics.S3.svg?style=flat&logo=nuget)](https://www.nuget.org/packages/jaytwo.Ergonomics.S3)
[![NuGet Downloads](https://img.shields.io/nuget/dt/jaytwo.Ergonomics.S3.svg?style=flat)](https://www.nuget.org/packages/jaytwo.Ergonomics.S3)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://mit-license.org/)

Source: [github.com/jakegough-jaytwo/jaytwo.Ergonomics.S3](https://github.com/jakegough-jaytwo/jaytwo.Ergonomics.S3)

Targets `net8.0`, `net6.0`, and `netstandard2.1`. Requires `AWSSDK.S3` **4.x** (v3 and v4 cannot be mixed in one app).

> **Pre-1.0.** The API is still settling; minor version bumps may break things until 1.0.

## Contents

- [Installation](#installation)
- [Quick start](#quick-start)
  - [Lifetime](#lifetime)
- [Configuration and dependency injection](#configuration-and-dependency-injection)
- [Why this exists](#why-this-exists)
- [Usage](#usage)
  - [Key prefixing](#key-prefixing)
  - [Put](#put)
  - [Multipart put (unknown length / large streams)](#multipart-put-unknown-length--large-streams)
  - [List](#list)
  - [Exists / not found](#exists--not-found)
  - [Get, head, delete, copy](#get-head-delete-copy)
  - [Buckets](#buckets)
  - [`IAmazonS3` extensions](#iamazons3-extensions)
  - [Keys and metadata](#keys-and-metadata)
  - [Constructing a raw `IAmazonS3`](#constructing-a-raw-iamazons3)
  - [Errors](#errors)
- [Logging and telemetry](#logging-and-telemetry)
- [Options reference](#options-reference)
- [API at a glance](#api-at-a-glance)
- [Development](#development)
- [Contributing](#contributing)
- [License](#license)

## Installation

```bash
dotnet add package jaytwo.Ergonomics.S3
```

## Quick start

```csharp
using System.Text;
using jaytwo.Ergonomics.S3;

var s3 = new S3Client(new S3ClientOptions
{
    ServiceUrl = "http://localhost:9000",   // omit for the regional AWS endpoint
    BucketName = "my-bucket",
    AccessKeyId = "minioadmin",             // omit for the SDK default credential chain
    SecretAccessKey = "minioadmin",
    KeyPrefix = "qa1/",                     // every key below is relative to this
});

await s3.PutObjectAsync("notes/hello.txt", Encoding.UTF8.GetBytes("hi")); // writes qa1/notes/hello.txt
var exists = await s3.ObjectExistsAsync("notes/hello.txt"); // true
using var file = await s3.GetObjectAsync("notes/hello.txt");
// file.Key, file.Size, file.ContentType, file.Body — one get for an HTTP response
var text = await s3.GetObjectStringAsync("notes/hello.txt"); // UTF-8 decode helper

await foreach (var item in s3.ListAllObjectsAsync("notes/"))
{
    // item is S3ObjectListItem: Key "notes/hello.txt", Size, LastModified, ETag
}
```

### Lifetime

`S3Client` is thread-safe and meant to be long-lived. The `IAmazonS3` underneath owns a pooled
HTTP stack that is expensive to build and cheap to share, so construct one client per bucket for
the life of the process rather than one per request. Don't mutate `Options` after construction.

`Dispose` only disposes the `IAmazonS3` when `S3Client` created it — a client handed to the
`S3Client(S3ClientOptions, IAmazonS3)` overload is left alone. That overload is also how you pass
a mock in tests. The `using` in the short examples further down is there because they are scripts,
not services.

## Configuration and dependency injection

`S3ClientOptions` is a plain settable POCO, so it binds straight from `IConfiguration`:

```json
{
  "S3": {
    "ServiceUrl": "http://localhost:9000",
    "BucketName": "my-bucket",
    "AccessKeyId": "minioadmin",
    "SecretAccessKey": "minioadmin",
    "KeyPrefix": "qa1/"
  }
}
```

```csharp
builder.Services.Configure<S3ClientOptions>(builder.Configuration.GetSection("S3"));

builder.Services.AddSingleton(sp =>
    new S3Client(sp.GetRequiredService<IOptions<S3ClientOptions>>().Value));
```

The container disposes the singleton at shutdown, so no `using` at the registration site.

Deployed to real AWS, drop `ServiceUrl` and the credentials and let the SDK default chain (instance
profile, IRSA, environment, …) do its job; the only thing that varies per environment is the prefix:

```bash
S3__KeyPrefix=qa2/
```

Same binary, same bucket, different namespace.

## Why this exists

It's not a new S3 SDK. It doesn't hide `IAmazonS3`, invent a multi-cloud store, or infer content types, ACLs, or storage classes. It is a thin layer on AWSSDK.S3 so applications can do the common operations — put, get, head, delete, list, exists — against a bucket-and-prefix they don't have to think about. MinIO and other custom `ServiceUrl` endpoints are first-class.

What you get:

- A bucket-and-prefix-scoped `S3Client`, plus `AmazonS3Extensions` for the operations the SDK makes you hand-roll
- Ergonomic result types for the common cases: `S3ObjectListItem` on list, `S3ObjectResponse` on get/head (headers + optional body), `S3PutObjectResponse` on put / multipart put (relative key, no bucket)
- Optional structured logging (`S3:*` events), Activities, and metrics on the `S3Client` you compose
- Opinionated defaults that otherwise leak into every call site: path-style when `ServiceUrl` is set, keys relative to `KeyPrefix`, and a single stream-ownership rule — streams you pass in are never closed, streams the library creates are always disposed
- An explicit escape hatch: `S3Client.AmazonS3` stays public; when you need the full SDK surface, use it

What you don't get:

- A fluent API — configure delegates hand you the real SDK request; C# object initializers already cover the rest
- Magic — no content-type sniffing, no tenant/layout policy, no mutating AWS SDK response objects.
  Multipart is opt-in via `PutObjectMultipartAsync` (ordinary `PutObjectAsync` stays single-request)
- A reason to stop using `PutObjectRequest` when you need the full surface

## Usage

### Key prefixing

The usual use case is one bucket shared by several instances of the same application. QA1, QA2, a PR environment, and a developer laptop all write to the same bucket, each confined to its own prefix. Application logic only ever knows about `notes/foo.txt`; whether that lands at `qa1/notes/foo.txt` or `qa2/notes/foo.txt` is deployment configuration, not code. The same binary ships everywhere and the prefix changes in `appsettings` or an environment variable.

That pattern exists because buckets are usually not cheap to mint. They are centrally provisioned, governed by IAM policy, and subject to naming and quota rules — so "one bucket per environment" is often not a decision the application team gets to make. Prefix multiplexing is how you get environment isolation inside a bucket you were handed. It also makes tests cheap: point a run at a unique prefix in a shared bucket and you get isolation without provisioning a bucket per run.

**Old Way** — every call site owns the prefix:

```csharp
private readonly string _prefix = "qa1/";

await amazonS3.PutObjectAsync(
    new PutObjectRequest
    {
        BucketName = _bucket,
        Key = _prefix + "notes/foo.txt",
        InputStream = stream,
        AutoCloseStream = false,
    },
    cancellationToken);

var response = await amazonS3.ListObjectsV2Async(
    new ListObjectsV2Request { BucketName = _bucket, Prefix = _prefix + "notes/" },
    cancellationToken);

foreach (var item in response.S3Objects ?? new List<S3Object>())
{
    var logicalKey = item.Key.Substring(_prefix.Length);
}
```

**New Way**

```csharp
await s3.PutObjectAsync("notes/foo.txt", stream, cancellationToken);

var page = await s3.ListObjectsAsync("notes/", cancellationToken: cancellationToken);

foreach (var item in page.Objects)
{
    // item.Key is already "notes/foo.txt" (S3ObjectListItem)
}
```

Prepending on write is the easy half. Stripping on read is the half people skip — and skipping it means the prefix leaks into API responses, log lines, and pagination cursors. A missed `_prefix` at one call site is silent cross-environment corruption: nothing throws, QA2 writes into QA1's namespace, and you find out later from data that should not exist.

`S3Client` applies `KeyPrefix` when building requests and projects relative keys onto the
types it returns (`S3ObjectListItem`, `S3ObjectResponse`, `S3PutObjectResponse`). It does not mutate AWS SDK objects.
`GetFullKey` / `GetRelativeKey` convert when you need a physical key outside the client.

### Put

`PutObjectRequest` expresses the body through mutually exclusive properties — `InputStream`,
`ContentBody`, and `FilePath` — and nothing in the type system says so. Taking the body as an
argument moves that choice to overload resolution (`Stream` or `byte[]`). Stream ownership also
has to disagree depending on who created the stream: callers keep theirs open; library-owned temp
streams are disposed after the upload completes.

**Old Way**

```csharp
using var ms = new MemoryStream(bytes, writable: false);
await amazonS3.PutObjectAsync(
    new PutObjectRequest
    {
        BucketName = "my-bucket",
        Key = "qa1/notes/hello.txt",
        InputStream = ms,
        ContentType = "text/plain",
        AutoCloseStream = false,
    },
    cancellationToken);
```

**New Way**

```csharp
await s3.PutObjectAsync("notes/hello.txt", bytes, "text/plain", cancellationToken);

// Stream — caller owns it (AutoCloseStream = false)
var put = await s3.PutObjectAsync("notes/hello.txt", stream, cancellationToken);
// put is S3PutObjectResponse: Key relative to KeyPrefix, ETag, VersionId — no bucket / full key
```

The configure delegate is for extras (metadata, checksums, …), not identity — `BucketName` and the
prefixed key are already set. It runs last, so it *can* overwrite them (including `ContentType`);
that isn't what it's for. Content type has dedicated overloads so the common case stays a string
argument — body shape (`Stream` / `byte[]`) keeps those overloads unambiguous.

Two stream overloads exist for the cases where the SDK cannot work it out on its own. A non-seekable
stream has no `Length`, so S3 needs `Content-Length` supplied up front. Pair length with content type
or configure when needed:

```csharp
await s3.PutObjectAsync("notes/hello.txt", networkStream, contentLength: 4096, cancellationToken);

await s3.PutObjectAsync(
    "notes/hello.txt",
    networkStream,
    contentLength: 4096,
    contentType: "application/octet-stream",
    cancellationToken);

await s3.PutObjectAsync(
    "notes/hello.txt",
    networkStream,
    contentLength: 4096,
    request => request.MD5Digest = Convert.ToBase64String(digestBytes),
    cancellationToken);
```

### Multipart put (unknown length / large streams)

Ordinary `PutObjectAsync` is a single request and needs a known length for non-seekable
streams (or a body under S3’s 5 GB single-PUT limit). For exports of unknown size — a CSV
streamed row-by-row from a database, possibly multi-GB — use `PutObjectMultipartAsync`.

It always runs Initiate → UploadPart(s) → Complete (no hybrid fall-back to single Put).
`partLength` is the seal threshold for each non-last part (default
`DefaultMultipartPartLength` / 8 MiB; must be at least `MinimumMultipartPartLength` / 5 MiB).
You never need the total object length: bytes are read into a part buffer of `partLength`,
uploaded when full, and the short remainder (or empty object) is the last part. Two buffers
are kept so the next part can be filled while the previous `UploadPart` is in flight — the
producer stays about one part behind instead of pausing for each S3 round-trip. There is no
spill to a temp file; memory cost is about `2 × partLength` per in-flight multipart put.
Callers own concurrency.

S3 caps a multipart upload at `MaxMultipartParts` (10,000), so the practical upper bound on
object size is about `partLength × 10,000`. With the 8 MiB default that is ~80 GiB; raise
`partLength` if you need more headroom (S3 allows parts up to 5 GiB). For typical large
reports that ceiling is remote; video or other multi-hundred-GB objects are where it matters.

```csharp
await s3.PutObjectMultipartAsync(
    "reports/export.csv",
    readableStream,
    partLength: S3Client.DefaultMultipartPartLength,
    contentType: "text/csv",
    cancellationToken);
```

The API takes a **readable** stream, same as other Puts. Report writers that want a
**writable** stream can bridge with `System.IO.Pipelines.Pipe` (dispose the writer when
done so the reader sees EOF):

```csharp
var pipe = new Pipe();
await using var readable = pipe.Reader.AsStream();
var writeTask = WriteCsvReportAsync(pipe.Writer.AsStream(), cancellationToken);

await s3.PutObjectMultipartAsync(
    "reports/export.csv",
    readable,
    partLength: 16L * 1024 * 1024,
    contentType: "text/csv",
    cancellationToken);

await writeTask;

async Task WriteCsvReportAsync(Stream writable, CancellationToken ct)
{
    await using (writable)
    {
        await foreach (var row in QueryRowsAsync(ct))
        {
            var line = Encoding.UTF8.GetBytes(FormatCsv(row) + "\n");
            await writable.WriteAsync(line, 0, line.Length, ct);
        }
    }
}
```

Configure runs on `InitiateMultipartUploadRequest` (ContentType, Metadata, …), not on each part.
Both single and multipart put return `S3PutObjectResponse` (relative `Key`, `ETag`, `VersionId`,
optional `ContentLength`; multipart also sets `PartCount` / `IsMultipart`).

### List

`ListObjectsV2` is paginated, and the loop is the same every time you write it. It is also easy to quietly walk a million keys. This package defaults to one page with a limit; walking the whole prefix has to be asked for by name.

**Old Way**

```csharp
var results = new List<S3Object>();
string? token = null;
do
{
    var response = await amazonS3.ListObjectsV2Async(
        new ListObjectsV2Request
        {
            BucketName = "my-bucket",
            Prefix = "qa1/notes/",
            ContinuationToken = token,
        },
        cancellationToken);

    foreach (var item in response.S3Objects ?? new List<S3Object>())
    {
        item.Key = item.Key.Substring("qa1/".Length);
        results.Add(item);
    }

    token = response.IsTruncated == true ? response.NextContinuationToken : null;
}
while (token is not null);
```

**New Way** — one page (`maxKeys` defaults to `S3Client.DefaultMaxKeys` / 1000):

```csharp
var page = await s3.ListObjectsAsync("notes/", maxKeys: 100, cancellationToken: cancellationToken);

foreach (var item in page.Objects)
{
    // S3ObjectListItem: Key relative to KeyPrefix, Size, LastModified, ETag
}

var next = page.NextContinuationToken; // null when complete; page.IsTruncated says the same thing
```

**New Way** — walk the whole prefix (opt-in; streams so stopping early stops the requests):

```csharp
await foreach (var item in s3.ListAllObjectsAsync("notes/", cancellationToken: cancellationToken))
{
    // one request per page until exhausted
}
```

`S3ObjectPage.Objects` is never null — each entry is an `S3ObjectListItem`, not an AWS `S3Object`.
`NextContinuationToken` is null exactly when there is nothing left to fetch, and it is S3's own
opaque token — passed through untouched in both directions, so it is not a key and has no
relationship to `KeyPrefix`.

`startAfter` *is* relative to `KeyPrefix`, on both `ListObjectsAsync` and `ListAllObjectsAsync`, so a service can accept a logical key as a resume point without re-prefixing at the boundary. It is ignored when `continuationToken` is set.

For delimiter / owner / encoding, use the configure overload — key-valued properties you set there (e.g. `StartAfter`) bypass the prefixing and must be full keys via `GetFullKey`:

```csharp
var page = await s3.ListObjectsAsync("notes/", list =>
{
    list.Delimiter = "/";
    list.StartAfter = s3.GetFullKey("notes/hello.txt");
});
```

### Exists / not found

The obvious `catch` filter on `StatusCode == NotFound` is subtly wrong against non-AWS implementations: a missing key can arrive as `NoSuchKey`, `NoSuchBucket`, or `NotFound` without the status code being set the way you expect. When the filter silently stops matching, a missing object becomes a 500.

**Old Way**

```csharp
try
{
    await amazonS3.GetObjectMetadataAsync(
        new GetObjectMetadataRequest { BucketName = "my-bucket", Key = "qa1/notes/hello.txt" },
        cancellationToken);
    return true;
}
catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    return false;
}
```

**New Way**

```csharp
return await s3.ObjectExistsAsync("notes/hello.txt", cancellationToken);
```

`AmazonS3Exception.IsNotFound()` covers a `404` status code, the `NoSuchKey`, `NoSuchBucket`, and `NotFound` error codes, and — as a last resort for implementations that set neither — messages containing `Not Found` or `404`. Use it in your own `catch` filters; `ObjectExistsAsync` and the GetOrNull helpers already do.

Two consequences of casting that wide a net, both deliberate:

- **A missing *bucket* reads as a missing object.** `ObjectExistsAsync` returns `false` rather than throwing when the bucket itself is gone or misspelled. If that distinction matters, check `BucketExistsAsync` at startup — see [Buckets](#buckets).
- **The message fallback is a heuristic.** An unrelated error whose text happens to contain `404` will match. If you need precision in a specific `catch`, test `ErrorCode` yourself.

### Get, head, delete, copy

Get and head share one result type — `S3ObjectResponse` — the same idea as `HttpResponseMessage`
for GET vs HEAD. Attributes are always there; `Body` is the object stream on get and `null` on
head. That means one get is enough to populate an HTTP response (content type, length, etag) and
stream the body. Prefer the byte/string helpers when you only want the content. OrNull variants
treat not-found as an expected miss.

**Old Way**

```csharp
using var response = await amazonS3.GetObjectAsync(
    new GetObjectRequest { BucketName = "my-bucket", Key = "qa1/notes/hello.txt" },
    cancellationToken);
```

**New Way**

```csharp
using var file = await s3.GetObjectAsync("notes/hello.txt", cancellationToken);
// file.Key, file.Size, file.ContentType, file.Headers, file.ETag, file.Body

var bytes = await s3.GetObjectBytesAsync("notes/hello.txt", cancellationToken);
var text = await s3.GetObjectStringAsync("notes/hello.txt", cancellationToken);

var maybe = await s3.GetObjectBytesOrNullAsync("notes/optional.txt", cancellationToken);

using var head = await s3.HeadObjectAsync("notes/hello.txt", cancellationToken);
// same shape as get; head.Body is null

await s3.DeleteObjectAsync("notes/hello.txt", cancellationToken);

await s3.CopyObjectAsync("notes/hello.txt", "notes/hello-copy.txt", cancellationToken);
```

`GetObjectAsync`, `HeadObjectAsync`, `DeleteObjectAsync`, and `CopyObjectAsync` also take a
configure delegate for range reads, version ids, metadata directives, and the rest of the SDK
request:

```csharp
using var range = await s3.GetObjectAsync("notes/hello.txt", get => get.ByteRange = new ByteRange(0, 1023));
```

`GetObjectStringAsync` decodes UTF-8. OrNull methods return
`null` on not-found (Debug `S3:OBJECT_NOT_FOUND`, metric success); throwing Get still logs Error
on a miss. `CopyObjectAsync` prefixes source and destination in the same bucket; override buckets
or metadata in the configure delegate when you need to.
### Buckets

```csharp
if (!await s3.BucketExistsAsync(cancellationToken))
{
    // the assigned bucket is missing
}
```

`BucketExistsAsync` operates on `Options.BucketName`. It lists buckets and matches by name (the MinIO-friendly path), not a head-bucket call — which means it needs `s3:ListAllMyBuckets`, a permission some locked-down IAM policies withhold. This client does not create buckets; the bucket is assigned before the process starts.

### `IAmazonS3` extensions

When you already have an `IAmazonS3` and don't want the bucket-scoped facade, `AmazonS3Extensions` fills in the gaps. First, the two checks the SDK doesn't ship:

```csharp
if (await amazonS3.ObjectExistsAsync("my-bucket", "notes/hello.txt")) { /* ... */ }
if (await amazonS3.BucketExistsAsync("my-bucket")) { /* ... */ }
```

Second, put overloads that take the body as an argument and leave your stream open
(`configure` is a separate overload so a cancellation token still works without a no-op):

```csharp
await amazonS3.PutObjectAsync("my-bucket", "notes/hello.txt", stream, cancellationToken);

await amazonS3.PutObjectAsync(
    "my-bucket",
    "notes/hello.txt",
    stream,
    put => put.ContentType = "text/plain",
    cancellationToken);
```

Third, a configure-delegate overload of each common operation, so you can build a request inline instead of declaring it:

```csharp
var response = await amazonS3.ListObjectsV2Async(list =>
{
    list.BucketName = "my-bucket";
    list.Prefix = "notes/";
});

// same shape for PutObjectAsync, GetObjectAsync, HeadObjectAsync, DeleteObjectAsync
```

No bucket or prefix scoping in any of these — you set everything. The `PutObjectAsync` overloads default `AutoCloseStream = false`; because they share a name with the SDK method, that difference is invisible at the call site. Prefer bucket-scoped `S3Client` in application code.

### Keys and metadata

`S3Key.NormalizePrefix`, `S3Key.Combine`, and `S3Key.StripPrefix` are the same prefix rules
`S3Client` uses when projecting into `S3ObjectListItem` / `S3ObjectResponse`, exposed for callers
that bypass the facade. Trailing slashes are normalized (`qa1`, `qa1/`, and `qa1//` are
equivalent). `StripPrefix` — and therefore `S3Client.GetRelativeKey` — throws
`InvalidOperationException` if a key does not start with the configured prefix, which is the
tripwire that turns a prefix mix-up into a failure instead of silent corruption.

`MetadataCollection.ToDictionary()` and `HeadersCollection.ToDictionary()` copy SDK collections
into ordinal-ignore-case dictionaries (also used for `S3ObjectResponse.Metadata` /
`S3ObjectResponse.Headers` on get/head).

### Constructing a raw `IAmazonS3`

If you want the endpoint, signing, and path-style defaults without the bucket-scoped facade — to register `IAmazonS3` in DI, say, or to reach an operation `S3Client` doesn't wrap — the factory is public:

```csharp
IAmazonS3 amazonS3 = AmazonS3ClientFactory.Create(new S3ClientOptions
{
    ServiceUrl = "http://localhost:9000",
    AccessKeyId = "minioadmin",
    SecretAccessKey = "minioadmin",
});
```

`BucketName` and `KeyPrefix` are ignored here; they only mean something to `S3Client`. You own the
returned client's lifetime. This is the same call `new S3Client(options)` makes internally, and the
client it produces can be handed to `new S3Client(options, amazonS3)` if you want both.

### Errors

| Situation | Exception |
| --- | --- |
| Null argument on any public member | `ArgumentNullException` |
| `S3ClientOptions.BucketName` null or empty at `S3Client` construction | `ArgumentException` |
| `maxKeys` below 1 on `ListObjectsAsync` | `ArgumentOutOfRangeException` |
| Key does not start with `KeyPrefix` in `StripPrefix` / `GetRelativeKey` | `InvalidOperationException` |
| Anything the service rejects | `AmazonS3Exception`, unchanged from the SDK |

## Logging and telemetry

`S3Client` is the instrumented path. Pass an optional `ILogger`. `AmazonS3Extensions` stay
uninstrumented. Domain types hold a client and name keys. Telemetry is already on the
instance you inject.

```csharp
var s3 = new S3Client(options, logger);
```

Null logger means log events are no-ops; Activities and metrics still run when a listener is
subscribed:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(S3Client.TelemetrySourceName))
    .WithMetrics(m => m.AddMeter(S3Client.TelemetrySourceName));
```

Events use the `S3:*` catalog via `jaytwo.Ergonomics.Logging` (`S3ObjectEventLogger`):

| Event | Level | When |
| --- | --- | --- |
| `S3:OBJECT_PUT` / `GET` / `HEAD` / `DELETED` / `COPIED` / `LISTED` | Debug | Success |
| `S3:OBJECT_MULTIPART_PUT` | Debug | Multipart put success (`part_count`, `content_length`) |
| `S3:OBJECT_STREAM_CLOSED` | Debug | `GetObjectAsync` / `S3ObjectResponse` body disposed |
| `S3:OBJECT_EXISTS` / `S3:OBJECT_NOT_FOUND` | Debug | `ObjectExistsAsync` or GetOrNull answered |
| `S3:BUCKET_EXISTS` | Debug | Bucket existence check |
| `S3:OPERATION_FAILED` | Error | Unexpected failure |
| `S3:CANCELLED` | Warning | Caller cancellation |

Structured fields include `s3_operation`, `bucket`, `key` (relative to `KeyPrefix`),
`key_prefix`, and `elapsed_time_seconds`. `operation_id` is only present on multi-event
operations: get headers + stream use share one id, and `ListAllObjectsAsync` pages share one
id. Single-event calls (put, multipart put, head, delete, copy, exists, one-page list, buckets, GetOrNull miss)
omit it.

**Get duration is two phases.** `GetObjectAsync` returns when headers are ready and the
body stream is open — `S3:OBJECT_GET` / `s3.get_object` is time-to-first-byte, same
shape as Head. Drain and hold time land on dispose of the `S3ObjectResponse` (or its
`Body`): `S3:OBJECT_STREAM_CLOSED` / `s3.get_object.use` with `bytes_read`.
An undisposed body never finishes the use metric. Byte/string helpers dispose the response after
draining, so they always close both phases. Put already covers the full upload
in one duration because the SDK call waits for the body.

A domain store composes `S3Client` and names the keys. Logging and metrics stay on the client.

```csharp
public class InvoiceStore
{
    private readonly S3Client _s3;

    public InvoiceStore(S3Client s3) => _s3 = s3;

    public Task PutPdfAsync(string invoiceId, Stream pdf, CancellationToken ct = default)
        => _s3.PutObjectAsync($"invoices/{invoiceId}.pdf", pdf, ct);
}
```

## Options reference

| Property | Role |
| --- | --- |
| `ServiceUrl` | Custom endpoint (MinIO, LocalStack, …). Null means the regional AWS endpoint. |
| `BucketName` | Required for `S3Client`. Ignored by `AmazonS3ClientFactory`. |
| `AccessKeyId` / `SecretAccessKey` | Explicit credentials. Omit `AccessKeyId` for the SDK default chain. |
| `KeyPrefix` | Optional instance prefix; trailing slashes are normalized. Ignored by `AmazonS3ClientFactory`. |
| `AuthenticationRegion` | Signing / endpoint region; defaults to `us-east-1`. |
| `ForcePathStyle` | Null means on when `ServiceUrl` is set, off otherwise. |

## API at a glance

Every `S3Client` object method takes a key or prefix relative to `KeyPrefix`, and every `S3Client`
method ends with an optional `CancellationToken`. Where a `configure` delegate exists, it is a
**separate overload** (never an optional middle parameter), so you can pass a token without a
no-op configure.

**`S3Client` — objects**

| Member | Notes |
| --- | --- |
| `PutObjectAsync(key, Stream)` | Caller keeps the stream open; returns `S3PutObjectResponse` |
| `PutObjectAsync(key, Stream, configure)` | |
| `PutObjectAsync(key, Stream, contentType)` | |
| `PutObjectAsync(key, Stream, contentType, configure)` | |
| `PutObjectAsync(key, Stream, contentLength?)` | For non-seekable streams; null skips length |
| `PutObjectAsync(key, Stream, contentLength?, configure)` | |
| `PutObjectAsync(key, Stream, contentLength?, contentType)` | |
| `PutObjectAsync(key, Stream, contentLength?, contentType, configure)` | |
| `PutObjectAsync(key, byte[])` | Library-created stream, disposed for you |
| `PutObjectAsync(key, byte[], configure)` | |
| `PutObjectAsync(key, byte[], contentType)` | |
| `PutObjectAsync(key, byte[], contentType, configure)` | |
| `PutObjectMultipartAsync(key, Stream)` | Always multipart; returns same `S3PutObjectResponse` (`PartCount` set) |
| `PutObjectMultipartAsync(key, Stream, partLength)` | `partLength` ≥ 5 MiB; max object ≈ `partLength × 10000` |
| `PutObjectMultipartAsync(key, Stream, contentType)` | |
| `PutObjectMultipartAsync(key, Stream, partLength, contentType)` | |
| `PutObjectMultipartAsync(key, Stream, configure)` | Configure `InitiateMultipartUploadRequest` |
| `PutObjectMultipartAsync(key, Stream, partLength, configure)` | |
| `PutObjectMultipartAsync(key, Stream, contentType, configure)` | |
| `PutObjectMultipartAsync(key, Stream, partLength, contentType, configure)` | |
| `MinimumMultipartPartLength` / `DefaultMultipartPartLength` / `MaxMultipartParts` | `5 MiB` / `8 MiB` (~80 GiB max) / `10000` |
| `GetObjectAsync(key)` | `S3ObjectResponse` with `Body` set |
| `GetObjectAsync(key, configure)` | |
| `GetObjectOrNullAsync(key)` | `null` on not-found; Debug miss |
| `GetObjectOrNullAsync(key, configure)` | |
| `GetObjectBytesAsync(key)` | Drains body; owns both get phases |
| `GetObjectBytesAsync(key, configure)` | |
| `GetObjectBytesOrNullAsync(key)` | |
| `GetObjectBytesOrNullAsync(key, configure)` | |
| `GetObjectStringAsync(key)` | UTF-8 body |
| `GetObjectStringAsync(key, configure)` | |
| `GetObjectStringOrNullAsync(key)` | |
| `GetObjectStringOrNullAsync(key, configure)` | |
| `HeadObjectAsync(key)` | HEAD — same `S3ObjectResponse`, `Body` null |
| `HeadObjectAsync(key, configure)` | |
| `DeleteObjectAsync(key)` | |
| `DeleteObjectAsync(key, configure)` | |
| `CopyObjectAsync(sourceKey, destinationKey)` | Same-bucket; both keys prefixed |
| `CopyObjectAsync(sourceKey, destinationKey, configure)` | |
| `ObjectExistsAsync(key)` | See [Exists / not found](#exists--not-found) |
| `ListObjectsAsync(prefix, maxKeys, continuationToken, startAfter)` | One page; all args optional |
| `ListObjectsAsync(prefix, configure)` | One page, full request control |
| `ListAllObjectsAsync(prefix, startAfter)` | `IAsyncEnumerable<S3ObjectListItem>`, unbounded |
| `DefaultMaxKeys` | `const int` = 1000 |

**`S3Client` — buckets and plumbing**

| Member | Notes |
| --- | --- |
| `BucketExistsAsync()` | Operates on `Options.BucketName` |
| `AmazonS3` | The underlying `IAmazonS3`; the escape hatch |
| `Options` / `BucketName` / `KeyPrefix` | `KeyPrefix` is the normalized form |
| `GetFullKey(key)` / `GetRelativeKey(fullKey)` | Prefix on / prefix off |
| `TelemetrySourceName` | `"jaytwo.Ergonomics.S3"` for OTel `AddSource` / `AddMeter` |
| ctor `(options [, logger])` | Optional instrumentation |

**Everything else**

| Type | Members |
| --- | --- |
| `S3ObjectListItem` | List row: `Key`, `Size`, `LastModified`, `ETag` |
| `S3PutObjectResponse` | Put / multipart put: relative `Key`, `ETag`, `VersionId`, `ContentLength?`, `PartCount?` |
| `S3ObjectResponse` | Get/head: list fields + `ContentType`, `Headers`, `Metadata`, `Body` (`null` on head); `IDisposable` |
| `S3ObjectPage` | `Objects` (`IReadOnlyList<S3ObjectListItem>`, never null), `NextContinuationToken`, `IsTruncated` |
| `S3Key` | `NormalizePrefix`, `Combine`, `StripPrefix` |
| `AmazonS3ClientFactory` | `Create(S3ClientOptions)` |
| `AmazonS3Extensions` | `ObjectExistsAsync`, `BucketExistsAsync`, `PutObjectAsync`, and configure-delegate overloads of put / get / head / delete / list |
| `AmazonS3ExceptionExtensions` | `IsNotFound()` |
| `MetadataCollectionExtensions` | `ToDictionary()` |
| `HeadersCollectionExtensions` | `ToDictionary()` |

## Development

Requires the .NET SDK and, for the MinIO happy-path tests, Docker. The `Makefile` is GNU make over
bash — on Windows run it from Git Bash or WSL, or use the plain `dotnet` commands shown alongside
each target.

```bash
dotnet tool restore   # installs reportgenerator and nugetcheck, needed by `make test`
dotnet build
```

Start a local MinIO (API on `localhost:39000`, console on `localhost:39001`):

```bash
make localdev
```

`cd minio && make` is the interactive variant: it tears the container down first (`-v`, so volumes
are dropped) and then follows the logs in the foreground. `make localdev` alone just brings it up.

`test/jaytwo.Ergonomics.S3.Tests/testsettings.json` points the happy-path suite (`Category=Minio`) at that endpoint (`UseMinioServer: true`). With MinIO up:

```bash
make test
# or: dotnet test
```

Unit-only (no Docker): `dotnet test --filter Category!=Minio`. Tear down MinIO with `make localdev-clean`.

CI (Jenkins, `enableTesterNet: true`) also exercises a compose `testernet` profile, where the tests
run inside a container and MinIO is reached as `http://minio:9000` via
`testsettings.testernet.json`. Reproducing it locally needs the builder image built first:

```bash
make docker-builder    # testernet-run executes this image
make testernet-up
make testernet-run
make testernet-clean
```

Pack a local package into `out/packed` with `make pack` or `make pack-beta`.

## License

[MIT](https://mit-license.org/). See [LICENSE](LICENSE).

---

Made with &hearts; by Jake
