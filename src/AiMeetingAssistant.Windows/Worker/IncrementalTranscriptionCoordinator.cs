using System.Threading.Channels;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Transcripts;

namespace AiMeetingAssistant.Windows.Worker;

public sealed record IncrementalTranscriptionOptions(
    string ModelPath,
    string ModelId,
    string ComputePreference,
    string? OpenVinoModelPath,
    string? OpenVinoRuntimePath,
    string? SileroVadPath,
    string? TorchXpuRuntimePath,
    string? DiarizationModelPath);

public sealed class IncrementalTranscriptionStatusEventArgs(
    string level, string message, string source, int chunkIndex, string? outputPath = null) : EventArgs
{
    public string Level { get; } = level;
    public string Message { get; } = message;
    public string Source { get; } = source;
    public int ChunkIndex { get; } = chunkIndex;
    public string? OutputPath { get; } = outputPath;
}

/// <summary>
/// Processes durable chunks in one low-priority FIFO. Queue pressure never
/// blocks capture; unqueued chunks remain on disk for final catch-up.
/// </summary>
public sealed class IncrementalTranscriptionCoordinator : IAsyncDisposable
{
    private sealed record Work(IncrementalAudioChunkReadyEventArgs? Chunk, IncrementalTranscriptionOptions? Options,
        TaskCompletionSource<bool>? Completion = null);
    private readonly PythonWorkerClient _worker;
    private readonly Channel<Work> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _pump;

    public IncrementalTranscriptionCoordinator(PythonWorkerClient worker, int queueCapacity = 32)
    {
        _worker = worker;
        _queue = Channel.CreateBounded<Work>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false
        });
        _pump = PumpAsync();
    }

    public event EventHandler<IncrementalTranscriptionStatusEventArgs>? StatusChanged;

    public bool TryQueue(IncrementalAudioChunkReadyEventArgs chunk, IncrementalTranscriptionOptions options)
    {
        var accepted = _queue.Writer.TryWrite(new(chunk, options));
        if (!accepted) Publish("WARNING", $"Live transcription backlog full; {chunk.Source} chunk {chunk.Index + 1} remains on disk for final processing.", chunk);
        return accepted;
    }

    public async Task<string> FinalizeSessionAsync(string sessionDirectory, IncrementalTranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        await DrainAsync(cancellationToken).ConfigureAwait(false);
        foreach (var chunk in DiscoverChunks(sessionDirectory))
        {
            var output = OutputPath(chunk);
            if (File.Exists(output)) continue;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _queue.Writer.WriteAsync(new(chunk, options, completion), cancellationToken).ConfigureAwait(false);
            if (!await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException($"Catch-up transcription failed for {chunk.Source} chunk {chunk.Index + 1}.");
        }

        Publish("AI", "Reconciling live transcript chunks on the meeting timeline...", SessionEvent(sessionDirectory));
        var merged = IncrementalTranscriptReconciler.Reconcile(sessionDirectory);
        var processing = Path.Combine(sessionDirectory, "processing");
        var mergedPath = Path.Combine(processing, "incremental-transcript-merged.json");
        TranscriptDocumentStore.ExportJsonAtomic(mergedPath, merged);
        var systemAudio = Directory.GetFiles(sessionDirectory, "system_audio_*.wav").SingleOrDefault()
            ?? throw new InvalidDataException("Final system-audio master is missing.");
        var outputPath = Path.Combine(processing, $"transcript_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{options.ModelId}_incremental.json");
        Publish("AI", "Running final full-meeting speaker analysis...", SessionEvent(sessionDirectory));
        var job = await _worker.StartIncrementalFinalizationAsync(mergedPath, systemAudio, outputPath,
            options.DiarizationModelPath, options.TorchXpuRuntimePath, options.ComputePreference, cancellationToken).ConfigureAwait(false);
        string? lastPhase = null;
        job = await _worker.WaitForTranscriptionAsync(job.JobId, TimeSpan.FromSeconds(1), status =>
        {
            if (lastPhase == status.Status) return;
            lastPhase = status.Status;
            Publish("AI", $"Incremental finalization: {DisplayPhase(status.Status)}", SessionEvent(sessionDirectory));
        }, cancellationToken).ConfigureAwait(false);
        if (job.Status != "completed") throw new InvalidOperationException(job.Error ?? $"Incremental finalization ended as {job.Status}.");
        CopyAtomic(outputPath, Path.Combine(processing, "transcript.json"));
        Publish("AI", $"Incremental transcript completed · {job.SegmentCount ?? 0} segment(s).", SessionEvent(sessionDirectory), outputPath);
        return outputPath;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        try { await _pump.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
        _shutdown.Dispose();
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var work in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                if (work.Chunk is null) { work.Completion?.TrySetResult(true); continue; }
                var success = await ProcessAsync(work, _shutdown.Token).ConfigureAwait(false);
                work.Completion?.TrySetResult(success);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> ProcessAsync(Work work, CancellationToken token)
    {
        var chunk = work.Chunk!;
        var outputPath = OutputPath(chunk);
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        if (File.Exists(outputPath))
        {
            Publish("AI", $"Reusing prepared {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}.", chunk, outputPath);
            return true;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
            Publish("AI", $"Preparing live transcript for {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}...", chunk);
            var options = work.Options!;
            var job = await _worker.StartTranscriptionAsync([chunk.AudioPath], options.ModelPath, outputPath,
                computePreference: options.ComputePreference, modelId: options.ModelId,
                openVinoModelPath: options.OpenVinoModelPath, openVinoRuntimePath: options.OpenVinoRuntimePath,
                sileroVadPath: options.SileroVadPath, torchXpuRuntimePath: options.TorchXpuRuntimePath,
                sourceLabels: [NormalizeSource(chunk.Source)], lowPriority: true, cancellationToken: token).ConfigureAwait(false);
            string? lastPhase = null;
            job = await _worker.WaitForTranscriptionAsync(job.JobId, TimeSpan.FromSeconds(1), status =>
            {
                if (status.Status == lastPhase) return;
                lastPhase = status.Status;
                Publish("AI", $"Live {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}: {DisplayPhase(status.Status)}", chunk);
            }, token).ConfigureAwait(false);
            if (job.Status == "completed")
            {
                Publish("AI", $"Live transcript prepared for {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}.", chunk, job.OutputPath ?? outputPath);
                return true;
            }
            else if (job.Status != "cancelled")
                Publish("WARNING", $"Live transcription failed for {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}: {job.Error ?? job.Status}", chunk);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            Publish("WARNING", $"Live transcription failed for {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}: {exception.Message}", chunk);
        }
        return false;
    }

    private void Publish(string level, string message, IncrementalAudioChunkReadyEventArgs chunk, string? outputPath = null)
    {
        try { StatusChanged?.Invoke(this, new(level, message, chunk.Source, chunk.Index, outputPath)); } catch { }
    }

    private static string DisplaySource(string source) => source == "microphone" ? "microphone" : "system audio";

    private async Task DrainAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.Writer.WriteAsync(new(null, null, completion), token).ConfigureAwait(false);
        await completion.Task.WaitAsync(token).ConfigureAwait(false);
    }

    private static IReadOnlyList<IncrementalAudioChunkReadyEventArgs> DiscoverChunks(string sessionDirectory)
    {
        var results = new List<IncrementalAudioChunkReadyEventArgs>();
        var chunkRoot = Path.Combine(sessionDirectory, "processing", "live-chunks");
        foreach (var sourceDirectory in Directory.GetDirectories(chunkRoot)
                     .Where(path => Path.GetFileName(path) == "system_audio" || Path.GetFileName(path).StartsWith("microphone", StringComparison.Ordinal)))
        {
            var source = Path.GetFileName(sourceDirectory);
            var manifestPath = Path.Combine(sourceDirectory, "chunks.json");
            var manifest = System.Text.Json.JsonSerializer.Deserialize<IncrementalAudioChunkManifest>(File.ReadAllText(manifestPath),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException($"Chunk manifest is empty for {source}.");
            results.AddRange(manifest.Chunks.Select(chunk => new IncrementalAudioChunkReadyEventArgs(sessionDirectory, source,
                chunk.Index, Path.Combine(Path.GetDirectoryName(manifestPath)!, chunk.FileName), chunk.StartSeconds, chunk.DurationSeconds)));
        }
        return results.OrderBy(item => item.Index).ThenBy(item => item.Source).ToArray();
    }

    private static string OutputPath(IncrementalAudioChunkReadyEventArgs chunk) => Path.Combine(chunk.SessionDirectory,
        "processing", "live-transcripts", chunk.Source, $"chunk_{chunk.Index:D6}.json");

    private static string NormalizeSource(string source) => source.StartsWith("microphone", StringComparison.Ordinal) ? "microphone" : source;

    private static IncrementalAudioChunkReadyEventArgs SessionEvent(string directory) => new(directory, "meeting", 0, "", 0, 0);

    private static void CopyAtomic(string source, string destination)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try { File.Copy(source, temporary, true); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string DisplayPhase(string status) => status switch
    {
        "queued" => "queued",
        "normalizing" => "preparing audio",
        "loading-model" => "loading speech model",
        "loading-openvino-model" => "loading Intel GPU model",
        "detecting-speech" => "detecting speech",
        "transcribing" => "transcribing",
        "diarizing" => "detecting speakers",
        "assigning-speakers" => "assigning speakers",
        "skipping-speakers" => "no remote speech detected; skipping speaker analysis",
        "reusing-transcription" => "reusing cached result",
        "completed" => "completed",
        _ => status
    };
}
