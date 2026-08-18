using System.Diagnostics;
using System.Text.Json;
using AiMeetingAssistant.Contracts;

namespace AiMeetingAssistant.Windows.Worker;

public sealed record WorkerPackageStatus(bool Installed, string? Version);
public sealed record WorkerComputeStatus(string Preference, string Mode, string ComputeType, int BatchSize,
    bool CudaAvailable, string? DeviceName, long? TotalVramBytes, string? TorchVersion,
    string? TorchCudaVersion, string? FallbackReason, IReadOnlyList<string> SupportedPreferences, string? Error);
public sealed record WorkerRuntimeDiagnostics(string PythonExecutable, string Platform,
    IReadOnlyDictionary<string, WorkerPackageStatus> Packages, WorkerComputeStatus Compute,
    bool FfmpegAvailable, bool MlReady, IReadOnlyList<string> MissingRequirements);
public sealed record WorkerHealthResult(string Status, string WorkerVersion, string PythonVersion,
    bool RuntimeSupported, bool MlReady, IReadOnlyList<string> Capabilities, WorkerRuntimeDiagnostics Diagnostics);
public sealed record TranscriptionJobStatus(string JobId, string Status, double Progress,
    string? OutputPath, int? SegmentCount, string? Device, string? Source, string? Error);

public sealed class PythonWorkerClient(string pythonExecutable, string scriptPath, TimeSpan? requestTimeout = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    // Native Torch/CUDA discovery can exceed ten seconds on the first Windows
    // start after installation or an antivirus scan. Protocol calls remain
    // bounded, but the health diagnostic gets a realistic cold-start budget.
    private readonly TimeSpan _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    private readonly Queue<string> _diagnostics = new();
    private Process? _process;
    private Task? _stderrPump;

    public async Task<WorkerHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("health.check", new { }, cancellationToken).ConfigureAwait(false);
        var payload = response.Payload;
        var diagnostics = payload.GetProperty("diagnostics");
        var packages = diagnostics.GetProperty("packages").EnumerateObject().ToDictionary(
            item => item.Name,
            item => new WorkerPackageStatus(
                item.Value.GetProperty("installed").GetBoolean(),
                item.Value.GetProperty("version").ValueKind == JsonValueKind.Null ? null : item.Value.GetProperty("version").GetString()));
        var compute = diagnostics.GetProperty("compute");
        return new(
            payload.GetProperty("status").GetString() ?? "unknown",
            payload.GetProperty("workerVersion").GetString() ?? "unknown",
            payload.GetProperty("pythonVersion").GetString() ?? "unknown",
            payload.GetProperty("runtimeSupported").GetBoolean(),
            payload.GetProperty("mlReady").GetBoolean(),
            payload.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToArray(),
            new(
                diagnostics.GetProperty("pythonExecutable").GetString() ?? "unknown",
                diagnostics.GetProperty("platform").GetString() ?? "unknown",
                packages,
                new(
                    compute.GetProperty("preference").GetString() ?? "automatic",
                    compute.GetProperty("mode").GetString() ?? "unknown",
                    compute.GetProperty("computeType").GetString() ?? "unknown",
                    compute.GetProperty("batchSize").GetInt32(),
                    compute.GetProperty("cudaAvailable").GetBoolean(),
                    compute.GetProperty("deviceName").ValueKind == JsonValueKind.Null ? null : compute.GetProperty("deviceName").GetString(),
                    compute.GetProperty("totalVramBytes").ValueKind == JsonValueKind.Null ? null : compute.GetProperty("totalVramBytes").GetInt64(),
                    compute.GetProperty("torchVersion").ValueKind == JsonValueKind.Null ? null : compute.GetProperty("torchVersion").GetString(),
                    compute.GetProperty("torchCudaVersion").ValueKind == JsonValueKind.Null ? null : compute.GetProperty("torchCudaVersion").GetString(),
                    compute.GetProperty("fallbackReason").ValueKind == JsonValueKind.Null ? null : compute.GetProperty("fallbackReason").GetString(),
                    compute.GetProperty("supportedPreferences").EnumerateArray().Select(item => item.GetString() ?? "unknown").ToArray(),
                    compute.TryGetProperty("error", out var computeError) ? computeError.GetString() : null),
                diagnostics.GetProperty("ffmpegAvailable").GetBoolean(),
                diagnostics.GetProperty("mlReady").GetBoolean(),
                diagnostics.GetProperty("missingRequirements").EnumerateArray().Select(item => item.GetString() ?? "unknown").ToArray()));
    }

    public async Task<TranscriptionJobStatus> StartTranscriptionAsync(IReadOnlyList<string> audioPaths,
        string modelPath, string outputPath, string? language = null, string computePreference = "automatic",
        string? diarizationModelPath = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("transcription.start", new { audioPaths, modelPath, outputPath, language, computePreference, diarizationModelPath }, cancellationToken).ConfigureAwait(false);
        return ParseTranscriptionStatus(response.Payload);
    }

    public async Task<TranscriptionJobStatus> GetTranscriptionStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("transcription.status", new { jobId }, cancellationToken).ConfigureAwait(false);
        return ParseTranscriptionStatus(response.Payload);
    }

    public async Task<TranscriptionJobStatus> CancelTranscriptionAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("transcription.cancel", new { jobId }, cancellationToken).ConfigureAwait(false);
        return ParseTranscriptionStatus(response.Payload);
    }

    public async Task<TranscriptionJobStatus> WaitForTranscriptionAsync(string jobId, TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            while (true)
            {
                var status = await GetTranscriptionStatusAsync(jobId, cancellationToken).ConfigureAwait(false);
                if (status.Status is "completed" or "failed" or "cancelled") return status;
                await Task.Delay(pollInterval ?? TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            try { await CancelTranscriptionAsync(jobId, CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private static TranscriptionJobStatus ParseTranscriptionStatus(JsonElement payload) => new(
        payload.GetProperty("jobId").GetString() ?? "unknown",
        payload.GetProperty("status").GetString() ?? "unknown",
        payload.GetProperty("progress").GetDouble(),
        payload.TryGetProperty("outputPath", out var output) ? output.GetString() : null,
        payload.TryGetProperty("segmentCount", out var count) ? count.GetInt32() : null,
        payload.TryGetProperty("device", out var device) ? device.GetString() : null,
        payload.TryGetProperty("source", out var source) ? source.GetString() : null,
        payload.TryGetProperty("error", out var error) ? error.GetString() : null);

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
