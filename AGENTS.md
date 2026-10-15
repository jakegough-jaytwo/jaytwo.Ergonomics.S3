# Repo Instructions

## Workflow

- Do not ask for permission before read-only exploration inside the repo. It is fine to inspect context with commands like `ls`, `grep`, `find`, `rg`, `Get-Content`, and `git diff`.
- Prefer read-only inspection first when gathering context.
- Only stop to ask before destructive actions, network access, or commands that require escalation outside the sandbox.
- Prefer minimal patches over full-file rewrites.

## File editing and formatting

- Preserve a file's existing line endings when editing.
- Honor `.editorconfig`, especially `end_of_line` and `insert_final_newline`, when present.
- If `end_of_line` is not specified, detect the file's dominant existing line ending and keep it.
- Do not introduce mixed line endings within a file.
- Do not normalize or rewrite line endings unless explicitly requested.
- Prefer ASCII in source and docs unless a file already intentionally uses non-ASCII characters. Avoid introducing typographic punctuation that can turn into mojibake.
- Keep comments sparse and practical. Short "why" comments are fine; avoid tutorial-style narration.

## Product charter

A thin ergonomic layer on AWSSDK.S3 (4.x). It is not a new S3 SDK or a multi-cloud store. See `README.md`.

- Do not hide `IAmazonS3`. `S3Client.AmazonS3` stays public as the escape hatch to the full SDK surface.
- No magic: no content-type sniffing, no inferred ACLs or storage classes, no tenant/layout policy, no mutating AWS SDK response objects.
- Keys are relative to `KeyPrefix` in and out (results, log fields). Path-style addressing when `ServiceUrl` is set. MinIO and other custom endpoints are first-class.
- Multipart is opt-in via `PutObjectMultipartAsync`. Ordinary `PutObjectAsync` stays single-request.
- `S3Client` is the instrumented path (logging, Activities, metrics). `AmazonS3Extensions` stay uninstrumented.

### Consuming jaytwo.Ergonomics.Logging

`jaytwo.Ergonomics.Logging` lives in its own repo (`../jaytwo.Ergonomics.Logging`) and is consumed here as a NuGet `PackageReference`. Do not edit its sources from this repo; change them there. Its charter and event conventions are in that repo's `README.md` and `AGENTS.md`.

When emitting events from S3:

- Pair `S3ObjectEventLogger` methods with the named `EventId` catalog in `Logging/EventIds.cs`. Create entries with `EventLogger.EventIdFromName` (jaytwo.StableHashing). Do not emit library events through ad-hoc `logger.BuildMessage`. Do not hand-number `EventId.Id`.
- Event methods that format, hash, or run `extraConfig` must guard with `IsEnabled` at the same level as the terminal helper.
- Stamp `operation_id` with `WithField`, and only on multi-event operations (get headers + stream use, `ListAllObjectsAsync` pages). Do not implement per-event properties as `EventLogger.Scope` or a MEL `BeginScope` around an operation; MEL scopes are factory-global and leak onto the caller.
- Field and placeholder names are `snake_case`. Queryable durations use a `_seconds` suffix only; do not mix `_ms` and `_seconds`. Human-format placeholders use a `_pretty` suffix (`content_length_pretty`, `bytes_read_pretty`).
- Log lines are human-readable (Go compact durations, IEC byte sizes). Fields are sortable and unambiguous (invariant `_seconds`, raw numbers). Do not use one format for both.
- Always write both the human line and `WithField` context. Do not configure sinks.

### Log message templates

The full rules are under "Message templates" in the Logging `README.md`. In brief:

- One constant template per event. Render a missing optional value as a marker in the same template (put renders `unknown size` for a null length); do not fork the template.
- No grammatical pluralization: no `{plural}`, no `(s)`. Always use the plural noun (`Listed 1 objects`, `in 13 parts`).
- A byte size goes in the sentence (via `FormatBytesPretty`, in parentheses after the duration) only when it is the bytes the event moved: put, multipart put, stream closed. `Put object in 6.91ms (54 KiB).` Head and get headers time a metadata round trip, so `content_length` stays on the field only.
- Keep the shape `<Verb> <noun> in {elapsed_time} (<detail>).` (or `after {elapsed_time}` for things that end rather than complete).

## Code style and structure

- Match the existing public API style: prefer small overload families that forward to one core implementation instead of collapsing everything into one clever method.
- Stay explicit rather than magical. Do not add inference or hidden behavior.
- Use expression-bodied members for trivial pass-through wrappers; use block bodies when there is meaningful control flow, disposal, logging, or validation.
- When logic is only relevant to one method, prefer a local helper or local function near the call site rather than extracting a broader private method.
- Use file-scoped namespace declarations (`namespace jaytwo.Ergonomics.S3;`), not block-scoped.
- Prefix private instance fields with underscore.
- Mark concrete implementation classes `internal` when they are not part of the intended public API surface.
- Mark leaf implementation classes and result types `sealed` unless there is an explicit reason to allow inheritance.
- Preserve nullable intent. Keep `?`, null guards, and `ArgumentNullException` checks consistent with surrounding code.
- Guard constructor parameters with the null-coalescing throw inline: `_x = x ?? throw new ArgumentNullException(nameof(x));`. Guard method arguments with `Guard.NotNull(value, nameof(value));`.
- StyleCop warnings are errors, nullable is on, and the public API has XML docs.

## API design

- Configure delegates hand the caller the real SDK request (`Action<PutObjectRequest> configureRequest`). Do not wrap SDK requests in builder objects or a fluent API.
- Async methods return `Task` / `Task<T>`, matching the AWS SDK they wrap.
- Result types (`S3ObjectResponse`, `S3PutObjectResponse`, `S3ObjectListItem`, `S3ObjectPage`) carry relative keys and never expose the bucket.
- Gate framework-specific APIs with `#if` conditionals. Keep the conditional blocks narrow to only the member that requires the newer API. Targets are `net8.0`, `net6.0`, and `netstandard2.1`.

## Resource management and async

- One stream-ownership rule: streams the caller passes in are never closed; streams the library creates are always disposed.
- An `S3ObjectResponse` body must close both get phases on dispose. Byte/string helpers dispose the response after draining.
- Prefer `finally` in `Dispose()` when the second cleanup must run even if the first throws.
- In library async code, continue using `ConfigureAwait(false)`.

## Tests

- Mirror the existing xUnit style: clear arrange/act/assert sections, descriptive behavior-oriented names, and separate cases instead of hiding them behind heavy abstraction.
- Name test methods as `MethodName_does_behavior`, not `MethodName_WhenCondition_ThenOutcome`.
- Use lowercase `// arrange`, `// act`, `// assert` section comments in every test method. Never omit a section - if a section has nothing to do, add a parenthetical note, e.g. `// arrange (no setup needed)` or `// act (no action needed)`. Always extract act calls into named variables; do not call the method under test inline inside an `Assert`.
- Unit tests mock `IAmazonS3` with `MockBehavior.Strict`. Tests that need a real endpoint go under `HappyPath/` with `[Trait("Category", "Minio")]`; `dotnet test --filter Category!=Minio` must pass without Docker.
- Assert log events through `CapturingLogger` records: the `EventId`, the level, the field bag, and the rendered message (`LogEntry.Message`). The rendered line starts with the `[EVENT_ID]` prefix, so anchor the regex on the closing `]` and match the duration loosely: `@"] Put object in \S+ \(2 B\)\.$"`.
- Cover the missing-value path when a message renders an optional value (e.g. a put from a non-seekable stream with no length renders `unknown size`).
