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
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(750);
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
            // Capture uses TryWrite and leaves overflow chunks on disk. Finalization
            // uses WriteAsync and must wait for space rather than lose completion work.
            FullMode = BoundedChannelFullMode.Wait,
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
        var catchUp = new List<Task<bool>>();
        foreach (var chunk in DiscoverChunks(sessionDirectory))
        {
            var output = OutputPath(chunk);
            if (File.Exists(output)) continue;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _queue.Writer.WriteAsync(new(chunk, options, completion), cancellationToken).ConfigureAwait(false);
            catchUp.Add(completion.Task);
        }
        if (catchUp.Count > 0 && (await Task.WhenAll(catchUp).WaitAsync(cancellationToken).ConfigureAwait(false)).Any(success => !success))
            throw new InvalidOperationException("One or more catch-up chunks could not be transcribed.");

        Publish("AI", "Reconciling live transcript chunks on the meeting timeline...", SessionEvent(sessionDirectory));
        var merged = IncrementalTranscriptReconciler.Reconcile(sessionDirectory);
        var processing = Path.Combine(sessionDirectory, "processing");
        var mergedPath = Path.Combine(processing, "incremental-transcript-merged.json");
        TranscriptDocumentStore.ExportJsonAtomic(mergedPath, merged);
        var canonicalPath = Path.Combine(processing, "transcript.json");
        var preliminaryPath = Path.Combine(processing, "preliminary", "transcript.json");
        TranscriptDocumentStore.ExportJsonAtomic(preliminaryPath, merged);
        CopyAtomic(preliminaryPath, canonicalPath);
        Publish("READY", $"Preliminary transcript ready · {merged.Segments.Count} segment(s); identifying speakers in background.",
            SessionEvent(sessionDirectory), canonicalPath);
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
        CopyAtomic(outputPath, canonicalPath);
        Publish("READY", $"Speaker-enriched transcript completed · {job.SegmentCount ?? 0} segment(s).",
            SessionEvent(sessionDirectory), canonicalPath);
        var cleanup = await Task.Run(() => IncrementalProcessingCleanup.AfterSuccessfulFinalization(sessionDirectory)).ConfigureAwait(false);
        Publish("INFO", $"Processing cleanup reclaimed {FormatBytes(cleanup.ReclaimedBytes)} across {cleanup.DeletedFiles} temporary file(s).",
            SessionEvent(sessionDirectory));
        foreach (var warning in cleanup.Warnings) Publish("WARNING", warning, SessionEvent(sessionDirectory));
        return canonicalPath;
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
            Work? pending = null;
            while (pending is not null || await _queue.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
            {
                var work = pending ?? await _queue.Reader.ReadAsync(_shutdown.Token).ConfigureAwait(false);
                pending = null;
                if (work.Chunk is null) { work.Completion?.TrySetResult(true); continue; }
                await Task.Delay(BatchWindow, _shutdown.Token).ConfigureAwait(false);
                Work? partner = null;
                if (_queue.Reader.TryRead(out var candidate))
                {
                    if (CanBatch(work, candidate)) partner = candidate;
                    else pending = candidate;
                }
                var batch = partner is null ? new[] { work } : new[] { work, partner };
                var success = await ProcessBatchAsync(batch, _shutdown.Token).ConfigureAwait(false);
                foreach (var item in batch) item.Completion?.TrySetResult(success);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> ProcessBatchAsync(IReadOnlyList<Work> batch, CancellationToken token)
    {
        var pending = batch.Where(work => work.Chunk is not null && !File.Exists(OutputPath(work.Chunk))).ToArray();
        foreach (var reused in batch.Except(pending))
        {
            if (reused.Chunk is null) continue;
            var chunk = reused.Chunk;
            var outputPath = OutputPath(chunk);
            Publish("AI", $"Reusing prepared {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}.", chunk, outputPath);
        }
        if (pending.Length == 0) return true;

        try
        {
            foreach (var work in pending)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath(work.Chunk!))!);
                Publish("AI", $"Preparing live transcript for {DisplaySource(work.Chunk!.Source)} chunk {work.Chunk.Index + 1}...", work.Chunk);
            }
            var options = pending[0].Options!;
            var batchOutput = pending.Length == 1
                ? OutputPath(pending[0].Chunk!)
                : BatchOutputPath(pending[0].Chunk!.SessionDirectory);
            var job = await _worker.StartTranscriptionAsync(pending.Select(work => work.Chunk!.AudioPath).ToArray(),
                options.ModelPath, batchOutput,
                computePreference: options.ComputePreference, modelId: options.ModelId,
                openVinoModelPath: options.OpenVinoModelPath, openVinoRuntimePath: options.OpenVinoRuntimePath,
                sileroVadPath: options.SileroVadPath, torchXpuRuntimePath: options.TorchXpuRuntimePath,
                sourceLabels: pending.Select(work => NormalizeSource(work.Chunk!.Source)).ToArray(),
                lowPriority: true, cancellationToken: token).ConfigureAwait(false);
            string? lastPhase = null;
            job = await _worker.WaitForTranscriptionAsync(job.JobId, TimeSpan.FromSeconds(1), status =>
            {
                if (status.Status == lastPhase) return;
                lastPhase = status.Status;
                var label = pending.Length == 1 ? DisplaySource(pending[0].Chunk!.Source) : "paired audio";
                Publish("AI", $"Live {label}: {DisplayPhase(status.Status)}", pending[0].Chunk!);
            }, token).ConfigureAwait(false);
            if (job.Status == "completed")
            {
                if (pending.Length > 1)
                {
                    var combined = TranscriptDocumentStore.Load(job.OutputPath ?? batchOutput);
                    IncrementalTranscriptBatchSplitter.Split(combined, pending.ToDictionary(
                        work => NormalizeSource(work.Chunk!.Source), work => OutputPath(work.Chunk!)));
                }
                foreach (var work in pending)
                {
                    var source = NormalizeSource(work.Chunk!.Source);
                    var skipped = job.SkippedSources?.Contains(source, StringComparer.Ordinal) == true;
                    Publish(skipped ? "INFO" : "AI", skipped
                            ? $"No usable signal in {DisplaySource(work.Chunk.Source)} chunk {work.Chunk.Index + 1}; Whisper was skipped."
                            : $"Live transcript prepared for {DisplaySource(work.Chunk.Source)} chunk {work.Chunk.Index + 1}.",
                        work.Chunk, OutputPath(work.Chunk));
                }
                return true;
            }
            else if (job.Status != "cancelled")
                Publish("WARNING", $"Live transcription failed: {job.Error ?? job.Status}", pending[0].Chunk!);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            Publish("WARNING", $"Live transcription failed: {exception.Message}", pending[0].Chunk!);
        }
        return false;
    }

    private static bool CanBatch(Work first, Work second) => first.Chunk is not null && second.Chunk is not null
        && first.Options == second.Options
        && first.Chunk.SessionDirectory == second.Chunk.SessionDirectory
        && NormalizeSource(first.Chunk.Source) != NormalizeSource(second.Chunk.Source);

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

    private static string BatchOutputPath(string sessionDirectory) => Path.Combine(sessionDirectory, "processing",
        "live-transcripts", ".batches", $"batch_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.json");

    private static string NormalizeSource(string source) => source.StartsWith("microphone", StringComparison.Ordinal) ? "microphone" : source;

    private static IncrementalAudioChunkReadyEventArgs SessionEvent(string directory) => new(directory, "meeting", 0, "", 0, 0);

    private static void CopyAtomic(string source, string destination)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try { File.Copy(source, temporary, true); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
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
