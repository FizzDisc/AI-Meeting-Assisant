using System.Text.Json;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Transcripts;

namespace AiMeetingAssistant.Core.Meetings;

public sealed record MeetingLibraryEntry(
    string SessionDirectory,
    string SessionId,
    DateTimeOffset StartedAtUtc,
    double? DurationMilliseconds,
    string Status,
    string SourceSummary,
    string AlignmentStatus,
    string? Diagnostic,
    string? TranscriptPath,
    bool CanTranscribe,
    int TranscriptCount = 0)
{
    public string StartedLabel => StartedAtUtc.ToLocalTime().ToString("g");
    public string DurationLabel => DurationMilliseconds is double duration
        ? TimeSpan.FromMilliseconds(duration).ToString(@"hh\:mm\:ss")
        : "--:--:--";
    public string TranscriptStatus => TranscriptPath is null ? "Not transcribed" : $"{Math.Max(TranscriptCount, 1)} run(s)";
}

public sealed record MeetingLibraryResult(IReadOnlyList<MeetingLibraryEntry> Sessions, IReadOnlyList<string> Issues);

public static class MeetingLibrary
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static MeetingLibraryResult Discover(string baseDirectory)
    {
        if (!Directory.Exists(baseDirectory)) return new([], []);
        var sessions = new List<MeetingLibraryEntry>();
        var issues = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(baseDirectory, "session_*", SearchOption.TopDirectoryOnly))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                issues.Add($"{Path.GetFileName(directory)} has no manifest.json.");
                sessions.Add(FromInvalidDirectory(directory, "Manifest missing"));
                continue;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<CaptureSessionManifest>(File.ReadAllText(manifestPath), Options)
                    ?? throw new InvalidDataException("Manifest is empty.");
                var availableStreams = manifest.Streams
                    .Where(stream => File.Exists(Path.Combine(directory, stream.RelativePath)) && new FileInfo(Path.Combine(directory, stream.RelativePath)).Length > 0)
                    .Select(stream => stream.Kind)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var transcript = Path.Combine(directory, "processing", "transcript.json");
                var hasTranscript = File.Exists(transcript);
                // The library overview only needs the number of runs. Parsing every
                // transcript here made opening the app proportional to every segment
                // ever recorded. Full metadata remains lazy-loaded for the selected run.
                var transcriptCount = TranscriptRunCatalog.Count(directory);
                sessions.Add(new(directory, manifest.SessionId, manifest.StartedAtUtc, manifest.DurationMilliseconds,
                    manifest.Status, FormatSources(availableStreams), manifest.Alignment?.Status ?? "unavailable",
                    manifest.Alignment?.Detail, hasTranscript ? transcript : null,
                    manifest.Status == "completed" && availableStreams.Contains("microphone") && availableStreams.Contains("system_audio"), transcriptCount));
            }
            catch (Exception exception)
            {
                issues.Add($"{Path.GetFileName(directory)} could not be read: {exception.Message}");
                sessions.Add(FromInvalidDirectory(directory, "Manifest unreadable"));
            }
        }

        return new(sessions.OrderByDescending(session => session.StartedAtUtc).ToArray(), issues);
    }

    public static void DeleteSession(string baseDirectory, string sessionDirectory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        var parent = Path.GetDirectoryName(target);
        if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(target).StartsWith("session_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a direct session workspace inside the capture library can be deleted.");
        if (!Directory.Exists(target))
            throw new DirectoryNotFoundException("The selected recording no longer exists.");
        Directory.Delete(target, recursive: true);
    }

    private static MeetingLibraryEntry FromInvalidDirectory(string directory, string diagnostic) =>
        new(directory, Path.GetFileName(directory), Directory.GetCreationTimeUtc(directory), null, "invalid", "No verified media",
            "unavailable", diagnostic, null, false);

    private static string FormatSources(IReadOnlySet<string> sources)
    {
        var labels = new List<string>();
        if (sources.Contains("screen")) labels.Add("Screen");
        if (sources.Contains("system_audio")) labels.Add("System audio");
        if (sources.Contains("microphone")) labels.Add("Microphone");
        return labels.Count == 0 ? "No verified media" : string.Join(" · ", labels);
    }
}
