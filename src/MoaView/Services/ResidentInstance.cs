using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MoaView.Services;

public readonly record struct ResidentRequest(bool Background, string? FilePath);

/// <summary>Owns the per-user, per-session resident process and forwards subsequent launch requests.</summary>
public sealed class ResidentInstance : IDisposable
{
    private const int MaxMessageBytes = 16 * 1024;
    private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ConnectionAttemptTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ServerRequestTimeout = TimeSpan.FromSeconds(5);

    private readonly Mutex? _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _stop;
    private Task? _listener;
    private Action<ResidentRequest>? _handler;
    private bool _disposed;

    public ResidentInstance(string? name = null)
    {
        using var windowsIdentity = WindowsIdentity.GetCurrent();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var identity = windowsIdentity.User?.Value
            ?? throw new InvalidOperationException("현재 Windows 사용자 ID를 확인할 수 없습니다.");
        var sessionId = process.SessionId;
        var logicalName = string.IsNullOrWhiteSpace(name) ? "MoaView.Resident" : name.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{identity}|{sessionId}|{logicalName}")));
        _mutex = new Mutex(false, $@"Local\MoaView.{hash}");
        try
        {
            try
            {
                IsPrimary = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                IsPrimary = true;
            }

            _pipeName = $"MoaView.{hash}";
            if (!IsPrimary)
            {
                _mutex.Dispose();
            }
        }
        catch
        {
            _mutex.Dispose();
            throw;
        }
    }

    public bool IsPrimary { get; }

    /// <summary>Starts accepting requests on a background pipe thread once the primary app can dispatch them.</summary>
    public void StartListening(Action<ResidentRequest> onRequest)
    {
        ArgumentNullException.ThrowIfNull(onRequest);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimary) throw new InvalidOperationException("보조 인스턴스는 요청을 수신할 수 없습니다.");
        if (_listener is not null) throw new InvalidOperationException("요청 수신기가 이미 시작되었습니다.");

        _handler = onRequest;
        _stop = new CancellationTokenSource();
        _listener = ListenAsync(_stop.Token);
    }

    /// <summary>Forwards one launch request to the owner, retrying while its listener starts.</summary>
    public bool TryForward(ResidentRequest request, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsPrimary) throw new InvalidOperationException("주 인스턴스는 요청을 전달할 수 없습니다.");
        if (!IsValid(request, out error)) return false;

        var deadline = DateTime.UtcNow + ForwardTimeout;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var attemptMs = (int)Math.Clamp(Math.Min(ConnectionAttemptTimeout.TotalMilliseconds, remaining.TotalMilliseconds), 1, 250);
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            try
            {
                using var connectTimeout = new CancellationTokenSource(attemptMs);
                client.ConnectAsync(attemptMs, connectTimeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException ex)
            {
                lastError = ex;
                Thread.Sleep(50);
                continue;
            }
            catch (TimeoutException ex)
            {
                lastError = ex;
                Thread.Sleep(50);
                continue;
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(50);
                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
                break;
            }

            try
            {
                using var ioTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(
                    Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds)));
                WriteMessageAsync(client, request, ioTimeout.Token).GetAwaiter().GetResult();
                var response = new byte[1];
                var gotResponse = ReadExactlyAsync(client, response, ioTimeout.Token).GetAwaiter().GetResult();
                if (gotResponse && response[0] == 1)
                {
                    error = null;
                    return true;
                }

                lastError = new IOException("기존 모아뷰가 요청을 수락하지 않았습니다.");
                break;
            }
            catch (OperationCanceledException ex)
            {
                lastError = ex;
                break;
            }
            catch (IOException ex)
            {
                lastError = ex;
                break;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
                break;
            }
        }

        error = $"실행 중인 모아뷰에 요청을 전달하지 못했습니다. {lastError?.Message}";
        return false;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    MaxMessageBytes, sizeof(byte));
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(ServerRequestTimeout);
                var request = await ReadMessageAsync(server, requestTimeout.Token).ConfigureAwait(false);
                if (request is null || !IsValid(request.Value, out _))
                {
                    await server.WriteAsync(new byte[] { 0 }, requestTimeout.Token).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    _handler?.Invoke(request.Value);
                    await server.WriteAsync(new byte[] { 1 }, requestTimeout.Token).ConfigureAwait(false);
                    await server.FlushAsync(requestTimeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    await server.WriteAsync(new byte[] { 0 }, requestTimeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A malformed or abandoned client timed out. Keep the resident listener available.
            }
            catch (IOException)
            {
                if (cancellationToken.IsCancellationRequested) break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task<ResidentRequest?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[sizeof(int)];
        if (!await ReadExactlyAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false)) return null;
        var length = BitConverter.ToInt32(lengthBuffer);
        if (length <= 0 || length > MaxMessageBytes) return null;

        var payload = new byte[length];
        if (!await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false)) return null;
        try
        {
            return JsonSerializer.Deserialize<ResidentRequest>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (EndOfStreamException) { return false; }
    }

    private static async Task WriteMessageAsync(Stream stream, ResidentRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request);
        if (payload.Length > MaxMessageBytes) throw new InvalidDataException("요청 크기가 허용 한도를 넘었습니다.");
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsValid(ResidentRequest request, out string? error)
    {
        if (request.FilePath is { Length: > 4096 } || request.FilePath?.IndexOf('\0') >= 0)
        {
            error = "전달할 파일 경로가 너무 길거나 올바르지 않습니다.";
            return false;
        }

        error = null;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_stop is not null)
        {
            _stop.Cancel();
            try { _listener?.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            _stop.Dispose();
        }

        if (IsPrimary)
        {
            _mutex!.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
