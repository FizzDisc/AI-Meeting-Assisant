using System.Threading.Channels;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Worker;

public sealed record IncrementalTranscriptionOptions(
    string ModelPath,
    string ModelId,
    string ComputePreference,
    string? OpenVinoModelPath,
    string? OpenVinoRuntimePath,
    string? SileroVadPath,
    string? TorchXpuRuntimePath);

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
    private sealed record Work(IncrementalAudioChunkReadyEventArgs Chunk, IncrementalTranscriptionOptions Options);
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
                await ProcessAsync(work, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessAsync(Work work, CancellationToken token)
    {
        var chunk = work.Chunk;
        var outputDirectory = Path.Combine(chunk.SessionDirectory, "processing", "live-transcripts", chunk.Source);
        var outputPath = Path.Combine(outputDirectory, $"chunk_{chunk.Index:D6}.json");
        if (File.Exists(outputPath))
        {
            Publish("AI", $"Reusing prepared {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}.", chunk, outputPath);
            return;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
            Publish("AI", $"Preparing live transcript for {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}...", chunk);
            var options = work.Options;
            var job = await _worker.StartTranscriptionAsync([chunk.AudioPath], options.ModelPath, outputPath,
                computePreference: options.ComputePreference, modelId: options.ModelId,
                openVinoModelPath: options.OpenVinoModelPath, openVinoRuntimePath: options.OpenVinoRuntimePath,
                sileroVadPath: options.SileroVadPath, torchXpuRuntimePath: options.TorchXpuRuntimePath,
                sourceLabels: [chunk.Source], lowPriority: true, cancellationToken: token).ConfigureAwait(false);
            string? lastPhase = null;
            job = await _worker.WaitForTranscriptionAsync(job.JobId, TimeSpan.FromSeconds(1), status =>
            {
                if (status.Status == lastPhase) return;
                lastPhase = status.Status;
                Publish("AI", $"Live {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}: {DisplayPhase(status.Status)}", chunk);
            }, token).ConfigureAwait(false);
            if (job.Status == "completed")
                Publish("AI", $"Live transcript prepared for {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}.", chunk, job.OutputPath ?? outputPath);
            else if (job.Status != "cancelled")
                Publish("WARNING", $"Live transcription failed for {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}: {job.Error ?? job.Status}", chunk);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Publish("WARNING", $"Live transcription failed for {DisplaySource(chunk.Source)} chunk {chunk.Index + 1}: {exception.Message}", chunk);
        }
    }

    private void Publish(string level, string message, IncrementalAudioChunkReadyEventArgs chunk, string? outputPath = null)
    {
        try { StatusChanged?.Invoke(this, new(level, message, chunk.Source, chunk.Index, outputPath)); } catch { }
    }

    private static string DisplaySource(string source) => source == "microphone" ? "microphone" : "system audio";

    private static string DisplayPhase(string status) => status switch
    {
        "queued" => "queued",
        "normalizing" => "preparing audio",
        "loading-model" => "loading speech model",
        "loading-openvino-model" => "loading Intel GPU model",
        "detecting-speech" => "detecting speech",
        "transcribing" => "transcribing",
        "reusing-transcription" => "reusing cached result",
        "completed" => "completed",
        _ => status
    };
}
