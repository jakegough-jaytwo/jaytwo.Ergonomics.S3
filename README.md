# jaytwo.Ergonomics.S3

Ergonomic helpers for AWSSDK.S3.

[![NuGet Version](https://img.shields.io/nuget/v/jaytwo.Ergonomics.S3.svg?style=flat&logo=nuget)](https://www.nuget.org/packages/jaytwo.Ergonomics.S3)
[![NuGet Downloads](https://img.shields.io/nuget/dt/jaytwo.Ergonomics.S3.svg?style=flat)](https://www.nuget.org/packages/jaytwo.Ergonomics.S3)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://mit-license.org/)

Source: [github.com/jakegough-jaytwo/jaytwo.Ergonomics.S3](https://github.com/jakegough-jaytwo/jaytwo.Ergonomics.S3)

Targets `net8.0`, `net6.0`, and `netstandard2.1`. Requires `AWSSDK.S3`.

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
  - [List](#list)
  - [Exists / not found](#exists--not-found)
  - [Get, head, delete](#get-head-delete)
  - [Buckets](#buckets)
  - [`IAmazonS3` extensions](#iamazons3-extensions)
  - [Keys and metadata](#keys-and-metadata)
  - [Constructing a raw `IAmazonS3`](#constructing-a-raw-iamazons3)
  - [Errors](#errors)
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
using jaytwo.Ergonomics.S3;

var s3 = new S3Client(new S3ClientOptions
{
    ServiceUrl = "http://localhost:9000",   // omit for the regional AWS endpoint
    BucketName = "my-bucket",
    AccessKeyId = "minioadmin",             // omit for the SDK default credential chain
    SecretAccessKey = "minioadmin",
    KeyPrefix = "qa1/",                     // every key below is relative to this
});

await s3.PutObjectAsync("notes/hello.txt", "hi");           // writes qa1/notes/hello.txt
var exists = await s3.ObjectExistsAsync("notes/hello.txt"); // true
using var response = await s3.GetObjectAsync("notes/hello.txt");

await foreach (var item in s3.ListAllObjectsAsync("notes/"))
{
    // item.Key is "notes/hello.txt" — the prefix is already stripped
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
- Opinionated defaults that otherwise leak into every call site: SigV4, path-style when `ServiceUrl` is set, keys relative to `KeyPrefix`, and a single stream-ownership rule — streams you pass in are never closed, streams the library creates are always disposed
- An explicit escape hatch: `S3Client.AmazonS3` stays public; when you need the full SDK surface, use it

What you don't get:

- A fluent API — configure delegates hand you the real SDK request; C# object initializers already cover the rest
- Magic — no content-type sniffing, no multipart unless you configure the SDK request, no tenant/layout policy
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
    // item.Key is already "notes/foo.txt"
}
```

Prepending on write is the easy half. Stripping on read is the half people skip — and skipping it means the prefix leaks into API responses, log lines, and pagination cursors. A missed `_prefix` at one call site is silent cross-environment corruption: nothing throws, QA2 writes into QA1's namespace, and you find out later from data that should not exist.

`S3Client` applies `KeyPrefix` on the way in and removes it on the way out. `GetFullKey` / `GetRelativeKey` convert when you need a physical key outside the client.

### Put

`PutObjectRequest` expresses the body through three mutually exclusive properties — `InputStream`, `ContentBody`, and `FilePath` — and nothing in the type system says so. Taking the body as an argument moves that choice to overload resolution. Stream ownership also has to disagree depending on who created the stream: callers keep theirs open; library-owned temp streams are disposed after the upload completes.

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
await s3.PutObjectAsync("notes/hello.txt", bytes, put => put.ContentType = "text/plain", cancellationToken);

// Stream — caller owns it (AutoCloseStream = false)
await s3.PutObjectAsync("notes/hello.txt", stream, cancellationToken);

// string — uses ContentBody (no UTF8.GetBytes + MemoryStream copy)
await s3.PutObjectAsync("notes/hello.txt", "hi", cancellationToken);
```

The configure delegate is for extras (content type, metadata, …), not identity — `BucketName` and the prefixed key are already set. It runs last, so it *can* overwrite them; that isn't what it's for.

Two stream overloads exist for the cases where the SDK cannot work it out on its own. A non-seekable
stream has no `Length`, so S3 needs `Content-Length` supplied up front, and an MD5 digest gets you
server-side integrity verification on the upload:

```csharp
await s3.PutObjectAsync("notes/hello.txt", networkStream, contentLength: 4096, cancellationToken);

// contentLength is nullable here — pass null to send only the digest
await s3.PutObjectAsync("notes/hello.txt", networkStream, contentLength: 4096, md5: digestBytes, cancellationToken);
```

`md5` is the raw 16-byte digest; it is base64-encoded into `Content-MD5` for you.

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
    // item.Key is already relative to KeyPrefix
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

`S3ObjectPage.Objects` is never null. `NextContinuationToken` is null exactly when there is nothing left to fetch, and it is S3's own opaque token — passed through untouched in both directions, so it is not a key and has no relationship to `KeyPrefix`.

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

`AmazonS3Exception.IsNotFound()` covers a `404` status code, the `NoSuchKey`, `NoSuchBucket`, and `NotFound` error codes, and — as a last resort for implementations that set neither — messages containing `Not Found` or `404`. Use it in your own `catch` filters; `ObjectExistsAsync` already does.

Two consequences of casting that wide a net, both deliberate:

- **A missing *bucket* reads as a missing object.** `ObjectExistsAsync` returns `false` rather than throwing when the bucket itself is gone or misspelled. If that distinction matters, check `BucketExistsAsync` at startup — see [Buckets](#buckets).
- **The message fallback is a heuristic.** An unrelated error whose text happens to contain `404` will match. If you need precision in a specific `catch`, test `ErrorCode` yourself.

### Get, head, delete

These have one body-less request shape, so the main value is the prefix scoping above — worth having for consistency once you are on `S3Client`.

**Old Way**

```csharp
using var response = await amazonS3.GetObjectAsync(
    new GetObjectRequest { BucketName = "my-bucket", Key = "qa1/notes/hello.txt" },
    cancellationToken);
```

**New Way**

```csharp
using var response = await s3.GetObjectAsync("notes/hello.txt", cancellationToken);

var metadata = await s3.GetObjectMetadataAsync("notes/hello.txt", cancellationToken);
await s3.DeleteObjectAsync("notes/hello.txt", cancellationToken);
```

Each of the three also takes a configure delegate for range reads, version ids, conditional headers, and the rest of the SDK request:

```csharp
using var range = await s3.GetObjectAsync("notes/hello.txt", get => get.ByteRange = new ByteRange(0, 1023));
```

### Buckets

```csharp
await s3.EnsureBucketExistsAsync();
// also: BucketExistsAsync, PutBucketAsync
```

All three operate on `Options.BucketName`. Existence lists buckets and matches by name (the MinIO-friendly path), not a head-bucket call — which means it needs `s3:ListAllMyBuckets`, a permission some locked-down IAM policies withhold.

### `IAmazonS3` extensions

When you already have an `IAmazonS3` and don't want the bucket-scoped facade, `AmazonS3Extensions` fills in the gaps. First, the two checks the SDK doesn't ship:

```csharp
if (await amazonS3.ObjectExistsAsync("my-bucket", "notes/hello.txt")) { /* ... */ }
if (await amazonS3.BucketExistsAsync("my-bucket")) { /* ... */ }
```

Second, a put that takes the body as an argument and leaves your stream open:

```csharp
await amazonS3.PutObjectAsync(
    "my-bucket",
    "notes/hello.txt",
    stream,
    put => put.ContentType = "text/plain");
```

Third, a configure-delegate overload of each common operation, so you can build a request inline instead of declaring it:

```csharp
var response = await amazonS3.ListObjectsV2Async(list =>
{
    list.BucketName = "my-bucket";
    list.Prefix = "notes/";
});

// same shape for PutObjectAsync, GetObjectAsync, GetObjectMetadataAsync, DeleteObjectAsync
```

No bucket or prefix scoping in any of these — you set everything. The `PutObjectAsync` overloads default `AutoCloseStream = false`; because they share a name with the SDK method, that difference is invisible at the call site. Prefer bucket-scoped `S3Client` in application code.

### Keys and metadata

`S3Key.NormalizePrefix`, `S3Key.Combine`, and `S3Key.StripPrefix` are the same prefix rules `S3Client` uses, exposed for callers that bypass it. Trailing slashes are normalized (`qa1`, `qa1/`, and `qa1//` are equivalent). `StripPrefix` — and therefore `S3Client.GetRelativeKey` — throws `InvalidOperationException` if a key does not start with the configured prefix, which is the tripwire that turns a prefix mix-up into a failure instead of silent corruption.

`MetadataCollection.ToDictionary()` copies object metadata into an ordinal-ignore-case `IDictionary<string, string>`.

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
method ends with an optional `CancellationToken`.

**`S3Client` — objects**

| Member | Notes |
| --- | --- |
| `PutObjectAsync(key, Stream)` | Caller keeps the stream open |
| `PutObjectAsync(key, Stream, contentLength)` | For non-seekable streams |
| `PutObjectAsync(key, Stream, contentLength?, md5)` | Raw 16-byte digest; base64-encoded for you |
| `PutObjectAsync(key, Stream, configure)` | |
| `PutObjectAsync(key, byte[] [, configure])` | Library-created stream, disposed for you |
| `PutObjectAsync(key, string [, configure])` | Uses `ContentBody` |
| `GetObjectAsync(key [, configure])` | Response is `IDisposable` |
| `GetObjectMetadataAsync(key [, configure])` | HEAD |
| `DeleteObjectAsync(key [, configure])` | |
| `ObjectExistsAsync(key)` | See [Exists / not found](#exists--not-found) |
| `ListObjectsAsync(prefix, maxKeys, continuationToken, startAfter)` | One page; all args optional |
| `ListObjectsAsync(prefix, configure)` | One page, full request control |
| `ListAllObjectsAsync(prefix, startAfter)` | `IAsyncEnumerable<S3Object>`, unbounded |
| `DefaultMaxKeys` | `const int` = 1000 |

**`S3Client` — buckets and plumbing**

| Member | Notes |
| --- | --- |
| `BucketExistsAsync()` / `PutBucketAsync()` / `EnsureBucketExistsAsync()` | Operate on `Options.BucketName` |
| `AmazonS3` | The underlying `IAmazonS3`; the escape hatch |
| `Options` / `BucketName` / `KeyPrefix` | `KeyPrefix` is the normalized form |
| `GetFullKey(key)` / `GetRelativeKey(fullKey)` | Prefix on / prefix off |

**Everything else**

| Type | Members |
| --- | --- |
| `S3ObjectPage` | `Objects` (never null), `NextContinuationToken`, `IsTruncated` |
| `S3Key` | `NormalizePrefix`, `Combine`, `StripPrefix` |
| `AmazonS3ClientFactory` | `Create(S3ClientOptions)` |
| `AmazonS3Extensions` | `ObjectExistsAsync`, `BucketExistsAsync`, `PutObjectAsync`, and configure-delegate overloads of put / get / head / delete / list |
| `AmazonS3ExceptionExtensions` | `IsNotFound()` |
| `MetadataCollectionExtensions` | `ToDictionary()` |

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
