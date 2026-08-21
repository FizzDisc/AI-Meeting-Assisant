using System.Text;
using System.Text.Json;

namespace AiMeetingAssistant.Core.Transcripts;

public sealed record TranscriptDocument(
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string? Language,
    IReadOnlyDictionary<string, string?>? DetectedLanguages,
    string? Device,
    string? ComputeType,
    int? BatchSize,
    string? ComputePreference,
    string? FallbackReason,
    IReadOnlyList<TranscriptSegment> Segments,
    bool? DiarizationEnabled = null,
    int? SpeakerCount = null,
    string? ModelId = null,
    long? ProcessingDurationMilliseconds = null,
    IReadOnlyDictionary<string, TranscriptAudioEvidence>? AudioEvidence = null,
    IReadOnlyList<string>? SkippedSources = null);

public sealed record TranscriptAudioEvidence(double DurationSeconds, double? PeakDbfs, double? RmsDbfs,
    double? MaximumWindowRmsDbfs, double ActiveSeconds, int AnalyzedWindows, bool HasUsableSignal);

public sealed record TranscriptSegment(double Start, double End, string Text, string? Source = null,
    string? Speaker = null, string? SpeakerAssignment = null, double? SpeakerOverlapRatio = null);

public static class TranscriptDocumentStore
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static TranscriptDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<TranscriptDocument>(File.ReadAllText(path), ReadOptions)
            ?? throw new InvalidDataException("Transcript JSON is empty.");
        if (document.SchemaVersion is < 1 or > 3)
            throw new InvalidDataException($"Unsupported transcript schema {document.SchemaVersion}.");
        if (document.Segments is null)
            throw new InvalidDataException("Transcript segments are missing.");
        foreach (var segment in document.Segments)
        {
            if (!double.IsFinite(segment.Start) || !double.IsFinite(segment.End) || segment.Start < 0 || segment.End < segment.Start)
                throw new InvalidDataException("Transcript contains an invalid timestamp range.");
            if (string.IsNullOrWhiteSpace(segment.Text))
                throw new InvalidDataException("Transcript contains an empty segment.");
        }
        return document with { Segments = document.Segments.OrderBy(segment => segment.Start).ThenBy(segment => segment.End).ToArray() };
    }

    public static void ExportJsonAtomic(string path, TranscriptDocument document) =>
        WriteAtomic(path, JsonSerializer.Serialize(document, WriteOptions));

    public static void ExportMarkdownAtomic(string path, TranscriptDocument document,
        IReadOnlyDictionary<string, string>? speakerNames = null)
    {
        var markdown = new StringBuilder()
            .AppendLine("# Meeting transcript")
            .AppendLine()
            .AppendLine($"- Created: {document.CreatedAtUtc:O}")
            .AppendLine($"- Language: {document.Language ?? "multiple/unknown"}")
            .AppendLine($"- Processing: {document.Device ?? "unknown"}/{document.ComputeType ?? "unknown"}")
            .AppendLine();
        foreach (var segment in document.Segments)
        {
            markdown.Append("## ").Append(FormatTimestamp(segment.Start)).Append(" · ")
                .Append(FormatSpeaker(segment, speakerNames)).Append(" · ").AppendLine(FormatSource(segment.Source))
                .AppendLine().AppendLine(segment.Text.Trim()).AppendLine();
        }
        WriteAtomic(path, markdown.ToString());
    }

    public static string FormatTimestamp(double seconds)
    {
        var value = TimeSpan.FromSeconds(seconds);
        return value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss\.fff") : value.ToString(@"mm\:ss\.fff");
    }

    public static string FormatSource(string? source) => source switch
    {
        "microphone" => "Microphone",
        "system_audio" => "System audio",
        null or "" => "Mixed audio",
        _ => source.Replace('_', ' ')
    };

    public static string FormatSpeaker(TranscriptSegment segment) => segment.SpeakerAssignment switch
    {
        "ambiguous" => "Ambiguous speaker",
        "unassigned" => "Unknown speaker",
        "not-run" => "Speaker analysis not run",
        _ when !string.IsNullOrWhiteSpace(segment.Speaker) => segment.Speaker,
        _ => "Unknown speaker"
    };

    public static string FormatSpeaker(TranscriptSegment segment, IReadOnlyDictionary<string, string>? speakerNames)
    {
        var technicalLabel = FormatSpeaker(segment);
        return segment.Speaker is not null && speakerNames is not null && speakerNames.TryGetValue(segment.Speaker, out var displayName)
            ? displayName
            : technicalLabel;
    }

    private static void WriteAtomic(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
