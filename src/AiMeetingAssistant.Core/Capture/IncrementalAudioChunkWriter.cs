using System.Text.Json;
using System.Threading.Channels;

namespace AiMeetingAssistant.Core.Capture;

public sealed record IncrementalAudioChunk(
    int Index,
    string FileName,
    long StartFrame,
    long FrameCount,
    double StartSeconds,
    double DurationSeconds);

public sealed record IncrementalAudioChunkManifest(
    int SchemaVersion,
    string Source,
    string Status,
    int SampleRate,
    int ChannelCount,
    double TargetChunkSeconds,
    long DroppedBufferCount,
    long InsertedSilenceFrames,
    IReadOnlyList<IncrementalAudioChunk> Chunks,
    string? ErrorMessage = null);

public sealed class IncrementalAudioChunkReadyEventArgs(
    string sessionDirectory, string source, int index, string audioPath,
    double startSeconds, double durationSeconds) : EventArgs
{
    public string SessionDirectory { get; } = sessionDirectory;
    public string Source { get; } = source;
    public int Index { get; } = index;
    public string AudioPath { get; } = audioPath;
    public double StartSeconds { get; } = startSeconds;
    public double DurationSeconds { get; } = durationSeconds;
}

/// <summary>
/// Creates immutable PCM16 WAV chunks without ever blocking the capture callback.
/// A bounded queue protects recording stability; missing buffers are represented as silence.
/// </summary>
public sealed class IncrementalAudioChunkWriter : IAsyncDisposable
{
    private sealed record Buffer(long FramePosition, int FrameCount, byte[] Data);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _source;
    private readonly string _sessionDirectory;
    private readonly string _directory;
    private readonly string _manifestPath;
    private readonly int _sampleRate;
    private readonly int _channelCount;
    private readonly int _bytesPerFrame;
    private readonly long _framesPerChunk;
    private readonly double _targetChunkSeconds;
    private readonly Channel<Buffer> _queue;
    private readonly Task _consumer;
    private readonly List<IncrementalAudioChunk> _chunks = [];
    private long _droppedBufferCount;
    private long _insertedSilenceFrames;
    private int _completionStarted;

    public IncrementalAudioChunkWriter(
        string sessionDirectory,
        string source,
        int sampleRate,
        int channelCount,
        double targetChunkSeconds = 30,
        int queueCapacity = 512)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount <= 0) throw new ArgumentOutOfRangeException(nameof(channelCount));
        if (targetChunkSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(targetChunkSeconds));
        if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));

        _source = source;
        _sessionDirectory = Path.GetFullPath(sessionDirectory);
        _sampleRate = sampleRate;
        _channelCount = channelCount;
        _bytesPerFrame = checked(channelCount * 2);
        _targetChunkSeconds = targetChunkSeconds;
        _framesPerChunk = checked((long)Math.Round(sampleRate * targetChunkSeconds));
        _directory = Path.Combine(sessionDirectory, "processing", "live-chunks", source);
        _manifestPath = Path.Combine(_directory, "chunks.json");
        Directory.CreateDirectory(_directory);

        _queue = Channel.CreateBounded<Buffer>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        WriteManifest("recording");
        _consumer = ConsumeAsync();
    }

    public long DroppedBufferCount => Interlocked.Read(ref _droppedBufferCount);
    public event EventHandler<IncrementalAudioChunkReadyEventArgs>? ChunkFinalized;

    public bool TryEnqueue(AudioFrameCapturedEventArgs frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var data = frame.Pcm16Data;
        if (data is null || data.Length == 0 || frame.FrameCount <= 0) return true;
        if (data.Length != checked(frame.FrameCount * _bytesPerFrame))
            return false;

        if (_queue.Writer.TryWrite(new(frame.FramePosition, frame.FrameCount, data))) return true;
        Interlocked.Increment(ref _droppedBufferCount);
        return false;
    }

    public async Task CompleteAsync()
    {
        if (Interlocked.Exchange(ref _completionStarted, 1) == 0) _queue.Writer.TryComplete();
        await _consumer.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await CompleteAsync().ConfigureAwait(false);

    private async Task ConsumeAsync()
    {
        Pcm16WavWriter? writer = null;
        string? temporaryPath = null;
        long expectedFrame = 0;
        long chunkStartFrame = 0;
        long framesInChunk = 0;
        try
        {
            await foreach (var buffer in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (buffer.FramePosition > expectedFrame)
                {
                    var missing = buffer.FramePosition - expectedFrame;
                    Interlocked.Add(ref _insertedSilenceFrames, missing);
                    while (missing > 0)
                    {
                        var batch = Math.Min(missing, _sampleRate);
                        missing -= batch;
                        WriteFrames(new byte[checked((int)batch * _bytesPerFrame)], ref batch);
                    }
                }

                var sourceOffset = buffer.FramePosition < expectedFrame
                    ? (int)Math.Min(buffer.FrameCount, expectedFrame - buffer.FramePosition)
                    : 0;
                var remaining = buffer.FrameCount - sourceOffset;
                var byteOffset = checked(sourceOffset * _bytesPerFrame);
                while (remaining > 0)
                {
                    EnsureWriter();
                    var count = (int)Math.Min(remaining, _framesPerChunk - framesInChunk);
                    writer!.Write(buffer.Data.AsSpan(byteOffset, checked(count * _bytesPerFrame)));
                    framesInChunk += count;
                    expectedFrame += count;
                    remaining -= count;
                    byteOffset += checked(count * _bytesPerFrame);
                    if (framesInChunk == _framesPerChunk) FinalizeChunk();
                }
            }

            if (writer is not null) FinalizeChunk();
            WriteManifest(DroppedBufferCount > 0 ? "degraded" : "completed");
        }
        catch (Exception exception)
        {
            writer?.Dispose();
            TryDelete(temporaryPath);
            WriteManifest("failed", exception.Message);
        }

        void WriteFrames(byte[] data, ref long frameCount)
        {
            var offset = 0;
            while (frameCount > 0)
            {
                EnsureWriter();
                var count = (int)Math.Min(frameCount, _framesPerChunk - framesInChunk);
                writer!.Write(data.AsSpan(offset, checked(count * _bytesPerFrame)));
                framesInChunk += count;
                expectedFrame += count;
                frameCount -= count;
                offset += checked(count * _bytesPerFrame);
                if (framesInChunk == _framesPerChunk) FinalizeChunk();
            }
        }

        void EnsureWriter()
        {
            if (writer is not null) return;
            chunkStartFrame = expectedFrame;
            temporaryPath = Path.Combine(_directory, $"chunk_{_chunks.Count:D6}.wav.partial");
            writer = new(temporaryPath, checked((ushort)_channelCount), checked((uint)_sampleRate));
        }

        void FinalizeChunk()
        {
            if (writer is null || temporaryPath is null || framesInChunk == 0) return;
            writer.Dispose();
            var fileName = $"chunk_{_chunks.Count:D6}.wav";
            File.Move(temporaryPath, Path.Combine(_directory, fileName), true);
            _chunks.Add(new(_chunks.Count, fileName, chunkStartFrame, framesInChunk,
                chunkStartFrame / (double)_sampleRate, framesInChunk / (double)_sampleRate));
            var completed = _chunks[^1];
            writer = null;
            temporaryPath = null;
            framesInChunk = 0;
            WriteManifest("recording");
            try
            {
                ChunkFinalized?.Invoke(this, new(_sessionDirectory, _source, completed.Index,
                    Path.Combine(_directory, fileName), completed.StartSeconds, completed.DurationSeconds));
            }
            catch
            {
                // Optional consumers must never fail the durable chunk writer.
            }
        }
    }

    private void WriteManifest(string status, string? errorMessage = null)
    {
        var manifest = new IncrementalAudioChunkManifest(1, _source, status, _sampleRate, _channelCount,
            _targetChunkSeconds, DroppedBufferCount, Interlocked.Read(ref _insertedSilenceFrames), [.. _chunks], errorMessage);
        var temporary = _manifestPath + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporary, _manifestPath, true);
    }

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch { }
    }
}
