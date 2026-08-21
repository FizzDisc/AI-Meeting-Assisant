using System.Diagnostics;
using AiMeetingAssistant.Core.Storage;

namespace AiMeetingAssistant.Windows.Storage;

public enum CaptureCompressionItemStatus { Completed, Failed, Skipped }
public sealed record CaptureCompressionProgress(int CompletedItems, int TotalItems, string StreamKind, string SourcePath, string Stage, double Percent);
public sealed record CaptureCompressionItemResult(string StreamKind, string SourcePath, string? ArchivePath, CaptureCompressionItemStatus Status, long SourceBytes, long ArchiveBytes, string? ErrorMessage)
{ public long SavedBytes => Status == CaptureCompressionItemStatus.Completed ? Math.Max(0, SourceBytes - ArchiveBytes) : 0; }
public sealed record CaptureCompressionResult(string SessionDirectory, IReadOnlyList<CaptureCompressionItemResult> Items)
{
    public int CompletedCount => Items.Count(x => x.Status == CaptureCompressionItemStatus.Completed);
    public int FailedCount => Items.Count(x => x.Status == CaptureCompressionItemStatus.Failed);
    public long SavedBytes => Items.Sum(x => x.SavedBytes);
}
public sealed record WaveMediaInfo(int SampleRate, int Channels, TimeSpan Duration);

public interface ICaptureCompressionMediaTool
{
    Task EncodeFlacAsync(string sourceWavePath, string temporaryFlacPath, CancellationToken cancellationToken);
    Task DecodeToWaveAsync(string flacPath, string temporaryWavePath, CancellationToken cancellationToken);
}

public sealed class CaptureFlacCompressor(ICaptureCompressionMediaTool mediaTool)
{
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromMilliseconds(100);

    public async Task<CaptureCompressionResult> CompressSessionAsync(string sessionDirectory, IProgress<CaptureCompressionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        var plan = CaptureCompressionPlanner.AnalyzeSession(session); // Revalidate immediately before execution.
        var results = new List<CaptureCompressionItemResult>();
        for (var index = 0; index < plan.Candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = plan.Candidates[index];
            var source = ResolvePlannedPath(session, candidate.SourcePath);
            var archive = ResolvePlannedPath(session, candidate.ProposedArchivePath);
            var tempFlac = $"{archive}.{Guid.NewGuid():N}.tmp";
            var decodedWave = $"{archive}.{Guid.NewGuid():N}.verify.wav";
            try
            {
                if (File.Exists(archive))
                {
                    results.Add(new(candidate.StreamKind, candidate.SourcePath, candidate.ProposedArchivePath, CaptureCompressionItemStatus.Skipped, candidate.SourceBytes, 0, "The FLAC archive already exists and was not overwritten."));
                    continue;
                }
                var sourceInfo = ReadWaveInfo(source);
                Report(progress, index, plan.Candidates.Count, candidate, "Encoding FLAC", .1);
                await mediaTool.EncodeFlacAsync(source, tempFlac, cancellationToken).ConfigureAwait(false);
                EnsureNonEmpty(tempFlac, "The encoder produced no FLAC data.");
                Report(progress, index, plan.Candidates.Count, candidate, "Verifying lossless decode", .65);
                await mediaTool.DecodeToWaveAsync(tempFlac, decodedWave, cancellationToken).ConfigureAwait(false);
                VerifyMedia(sourceInfo, ReadWaveInfo(decodedWave));
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(tempFlac, archive, false); // Atomic sibling promotion; WAV remains untouched.
                results.Add(new(candidate.StreamKind, candidate.SourcePath, candidate.ProposedArchivePath, CaptureCompressionItemStatus.Completed, candidate.SourceBytes, new FileInfo(archive).Length, null));
                progress?.Report(new(index + 1, plan.Candidates.Count, candidate.StreamKind, candidate.SourcePath,
                    "Verified", (index + 1d) / plan.Candidates.Count));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            { results.Add(new(candidate.StreamKind, candidate.SourcePath, candidate.ProposedArchivePath, CaptureCompressionItemStatus.Failed, candidate.SourceBytes, 0, exception.Message)); }
            finally { DeleteTemporary(tempFlac); DeleteTemporary(decodedWave); }
        }
        return new(session, results);
    }

    private static void VerifyMedia(WaveMediaInfo source, WaveMediaInfo decoded)
    {
        if (source.SampleRate != decoded.SampleRate) throw new InvalidDataException($"Sample-rate verification failed ({source.SampleRate} != {decoded.SampleRate}).");
        if (source.Channels != decoded.Channels) throw new InvalidDataException($"Channel verification failed ({source.Channels} != {decoded.Channels}).");
        if ((source.Duration - decoded.Duration).Duration() > DurationTolerance) throw new InvalidDataException($"Duration verification failed ({source.Duration} != {decoded.Duration}).");
    }

    public static WaveMediaInfo ReadWaveInfo(string path)
    {
        using var input = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        if (new string(input.ReadChars(4)) != "RIFF") throw new InvalidDataException("Media is not a RIFF WAV file.");
        _ = input.ReadUInt32();
        if (new string(input.ReadChars(4)) != "WAVE") throw new InvalidDataException("Media is not a WAVE file.");
        ushort channels = 0, blockAlign = 0; uint sampleRate = 0, dataBytes = 0;
        while (input.BaseStream.Position + 8 <= input.BaseStream.Length)
        {
            var id = new string(input.ReadChars(4)); var size = input.ReadUInt32();
            var next = checked(input.BaseStream.Position + size + (size & 1));
            if (next > input.BaseStream.Length + 1) throw new InvalidDataException("WAV chunk exceeds the file length.");
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("WAV format chunk is incomplete.");
                _ = input.ReadUInt16(); channels = input.ReadUInt16(); sampleRate = input.ReadUInt32(); _ = input.ReadUInt32(); blockAlign = input.ReadUInt16(); _ = input.ReadUInt16();
            }
            else if (id == "data") dataBytes = size;
            input.BaseStream.Position = Math.Min(next, input.BaseStream.Length);
        }
        if (channels == 0 || sampleRate == 0 || blockAlign == 0 || dataBytes == 0) throw new InvalidDataException("WAV metadata or audio payload is missing.");
        return new((int)sampleRate, channels, TimeSpan.FromSeconds(dataBytes / (double)(sampleRate * blockAlign)));
    }

    private static string ResolvePlannedPath(string session, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(session, relative));
        if (!full.StartsWith(session + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Compression path leaves the validated session workspace.");
        return full;
    }
    private static void EnsureNonEmpty(string path, string message) { if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException(message); }
    private static void DeleteTemporary(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    private static void Report(IProgress<CaptureCompressionProgress>? progress, int completed, int total, CaptureCompressionCandidate candidate, string stage, double itemProgress) =>
        progress?.Report(new(completed, total, candidate.StreamKind, candidate.SourcePath, stage, total == 0 ? 1 : Math.Clamp((completed + itemProgress) / total, 0, 1)));
}

public sealed class FfmpegCaptureCompressionMediaTool : ICaptureCompressionMediaTool
{
    public string ExecutablePath { get; }
    public FfmpegCaptureCompressionMediaTool(string? baseDirectory = null) => ExecutablePath = FfmpegRuntimeResolver.Resolve(baseDirectory);
    public Task EncodeFlacAsync(string source, string output, CancellationToken token) => RunAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-i", source, "-map", "0:a:0", "-c:a", "flac", "-compression_level", "8", "-f", "flac", output], token);
    public Task DecodeToWaveAsync(string source, string output, CancellationToken token) => RunAsync(["-hide_banner", "-loglevel", "error", "-nostdin", "-i", source, "-map", "0:a:0", "-c:a", "pcm_s16le", "-f", "wav", output], token);
    private async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg could not be started.");
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } throw; }
        var error = (await stderr.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0) throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}: {(error.Length == 0 ? "No diagnostic output." : error)}");
    }
}

public static class FfmpegRuntimeResolver
{
    public const string OverrideVariable = "AI_MEETING_ASSISTANT_FFMPEG";
    public static string Resolve(string? baseDirectory = null)
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return RequireFile(configured, "configured FFmpeg runtime");
        var directory = new DirectoryInfo(baseDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var relative in new[] { "ffmpeg.exe", Path.Combine("tools", "ffmpeg", "ffmpeg.exe"), Path.Combine("worker", "ffmpeg.exe") })
            { var candidate = Path.Combine(directory.FullName, relative); if (File.Exists(candidate)) return Path.GetFullPath(candidate); }
            directory = directory.Parent;
        }
        var fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var item in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        { var candidate = Path.Combine(item.Trim('"'), fileName); if (File.Exists(candidate)) return Path.GetFullPath(candidate); }
        throw new FileNotFoundException("FFmpeg was not found. Install it or set AI_MEETING_ASSISTANT_FFMPEG to ffmpeg.exe.");
    }
    private static string RequireFile(string path, string label)
    { var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim('"'))); return File.Exists(full) ? full : throw new FileNotFoundException($"The {label} does not exist: {full}", full); }
}
