using System.Text.Json;
using AiMeetingAssistant.Core.Transcripts;

namespace AiMeetingAssistant.Core.Storage;

public sealed record PcmMasterRemovalCandidate(string Kind, string WavePath, string FlacPath, long WaveBytes);
public sealed record PcmMasterRemovalPlan(string SessionDirectory, IReadOnlyList<PcmMasterRemovalCandidate> Candidates, string? BlockReason)
{
    public long ReclaimableBytes => Candidates.Sum(item => item.WaveBytes);
}
public sealed record PcmMasterRemovalResult(int DeletedFiles, long ReclaimedBytes);

public static class PcmMasterRemoval
{
    public static PcmMasterRemovalPlan Preview(string sessionDirectory)
    {
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        var sources = SessionAudioSourceResolver.Resolve(session);
        if (sources.Count != 2 || sources.Any(source => !source.UsesArchive))
            return new(session, [], "Both verified FLAC tracks are required.");
        IEnumerable<string> transcripts = Directory.Exists(Path.Combine(session, "processing"))
            ? Directory.EnumerateFiles(Path.Combine(session, "processing"), "transcript_*.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc)
            : Enumerable.Empty<string>();
        foreach (var transcript in transcripts)
        {
            if (!TranscriptProvesSources(transcript, sources)) continue;
            var candidates = sources.Select(source => new PcmMasterRemovalCandidate(source.Kind,
                    Path.ChangeExtension(source.Path, ".wav"), source.Path,
                    File.Exists(Path.ChangeExtension(source.Path, ".wav")) ? new FileInfo(Path.ChangeExtension(source.Path, ".wav")).Length : 0))
                .Where(item => item.WaveBytes > 0).ToArray();
            return candidates.Length == 0 ? new(session, [], "PCM masters have already been removed.") : new(session, candidates, null);
        }
        return new(session, [], "No completed transcript proves successful processing from these FLAC archives.");
    }

    public static PcmMasterRemovalResult Execute(string sessionDirectory)
    {
        var plan = Preview(sessionDirectory);
        if (plan.Candidates.Count == 0) throw new InvalidOperationException(plan.BlockReason ?? "No PCM masters are eligible.");
        long bytes = 0; var files = 0;
        foreach (var item in plan.Candidates)
        {
            if (!File.Exists(item.FlacPath) || !File.Exists(item.WavePath)) continue;
            File.Delete(item.WavePath); bytes += item.WaveBytes; files++;
        }
        return new(files, bytes);
    }

    private static bool TranscriptProvesSources(string transcriptPath, IReadOnlyList<SessionAudioSource> sources)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(transcriptPath));
            if (!document.RootElement.TryGetProperty("sourceMedia", out var media) || media.ValueKind != JsonValueKind.Array) return false;
            return sources.All(source => media.EnumerateArray().Any(item =>
                item.TryGetProperty("source", out var kind) && string.Equals(kind.GetString(), source.Kind, StringComparison.OrdinalIgnoreCase) &&
                item.TryGetProperty("path", out var path) && string.Equals(Path.GetFullPath(path.GetString() ?? ""), source.Path, StringComparison.OrdinalIgnoreCase) &&
                item.TryGetProperty("size", out var size) && size.GetInt64() == new FileInfo(source.Path).Length));
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException) { return false; }
    }
}
