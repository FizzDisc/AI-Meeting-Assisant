using System.Diagnostics;
using System.Text.Json;
using AiMeetingAssistant.Contracts;

namespace AiMeetingAssistant.Windows.Worker;

public sealed record WorkerHealthResult(string Status, string WorkerVersion, string PythonVersion, bool RuntimeSupported, IReadOnlyList<string> Capabilities);

public sealed class PythonWorkerClient(string pythonExecutable, string scriptPath, TimeSpan? requestTimeout = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly TimeSpan _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
    private readonly Queue<string> _diagnostics = new();
    private Process? _process;
    private Task? _stderrPump;

    public async Task<WorkerHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("health.check", new { }, cancellationToken).ConfigureAwait(false);
        var payload = response.Payload;
        return new(
            payload.GetProperty("status").GetString() ?? "unknown",
            payload.GetProperty("workerVersion").GetString() ?? "unknown",
            payload.GetProperty("pythonVersion").GetString() ?? "unknown",
            payload.GetProperty("runtimeSupported").GetBoolean(),
            payload.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToArray());
    }

    public async Task<WorkerResponse> SendAsync(string type, object payload, CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureStarted();
            var requestId = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(new { protocolVersion = WorkerProtocol.CurrentVersion, requestId, type, payload });
            await _process!.StandardInput.WriteLineAsync(request).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask().WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Python worker exited without a response. {GetDiagnostics()}");
            var response = JsonSerializer.Deserialize<WorkerResponse>(line) ?? throw new InvalidDataException("Python worker returned an empty response.");
            if (response.ProtocolVersion != WorkerProtocol.CurrentVersion) throw new InvalidDataException($"Worker protocol {response.ProtocolVersion} is incompatible with {WorkerProtocol.CurrentVersion}.");
            if (response.RequestId != requestId) throw new InvalidDataException("Python worker response request ID does not match.");
            if (!response.Ok) throw new InvalidOperationException($"Worker {response.Error?.Code}: {response.Error?.Message}");
            return response;
        }
        finally { _requestLock.Release(); }
    }

    private void EnsureStarted()
    {
        if (_process is { HasExited: false }) return;
        if (!File.Exists(scriptPath)) throw new FileNotFoundException("Python worker script was not found.", scriptPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(scriptPath);
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Python worker process could not be started.");
        _stderrPump = PumpDiagnosticsAsync(_process.StandardError);
    }

    private async Task PumpDiagnosticsAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lock (_diagnostics)
            {
                _diagnostics.Enqueue(line);
                while (_diagnostics.Count > 10) _diagnostics.Dequeue();
            }
        }
    }

    private string GetDiagnostics() { lock (_diagnostics) return string.Join(" | ", _diagnostics); }

    public async ValueTask DisposeAsync()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try { process.StandardInput.Close(); } catch { }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        if (_stderrPump is not null) { try { await _stderrPump.ConfigureAwait(false); } catch { } }
        process.Dispose();
        _requestLock.Dispose();
    }
}
