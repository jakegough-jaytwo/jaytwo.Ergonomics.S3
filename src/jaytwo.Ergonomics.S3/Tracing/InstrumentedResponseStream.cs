using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.Ergonomics.S3.Tracing;

/// <summary>
/// Counts bytes read and reports hold/transfer time when disposed. Used to split
/// GetObject headers (TTFB) from stream use, mirroring Ado's reader hold time.
/// </summary>
internal sealed class InstrumentedResponseStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long, Exception?> _onClosed;
    private long _bytesRead;
    private Exception? _fault;
    private int _closed;

    public InstrumentedResponseStream(Stream inner, Action<long, Exception?> onClosed)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onClosed = onClosed ?? throw new ArgumentNullException(nameof(onClosed));
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken)
        => _inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
        => _inner.Write(buffer, offset, count);

    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            var n = _inner.Read(buffer, offset, count);
            if (n > 0)
            {
                _bytesRead += n;
            }

            return n;
        }
        catch (Exception ex)
        {
            _fault = ex;
            throw;
        }
    }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    public override int Read(Span<byte> buffer)
    {
        try
        {
            var n = _inner.Read(buffer);
            if (n > 0)
            {
                _bytesRead += n;
            }

            return n;
        }
        catch (Exception ex)
        {
            _fault = ex;
            throw;
        }
    }
#endif

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        try
        {
            var n = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            if (n > 0)
            {
                _bytesRead += n;
            }

            return n;
        }
        catch (Exception ex)
        {
            _fault = ex;
            throw;
        }
    }

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            var n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (n > 0)
            {
                _bytesRead += n;
            }

            return n;
        }
        catch (Exception ex)
        {
            _fault = ex;
            throw;
        }
    }
#endif

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            base.Dispose(disposing);
            return;
        }

        if (disposing)
        {
            try
            {
                _onClosed(_bytesRead, _fault);
            }
            catch
            {
                // Telemetry must not break stream cleanup.
            }

            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
