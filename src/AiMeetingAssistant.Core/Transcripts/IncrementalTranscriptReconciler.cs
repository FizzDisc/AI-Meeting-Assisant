using System.Text.Json;
using System.Text.RegularExpressions;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Core.Transcripts;

public static partial class IncrementalTranscriptReconciler
{
    private sealed record Candidate(TranscriptSegment Segment, int ChunkIndex);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static TranscriptDocument Reconcile(string sessionDirectory)
    {
        var root = Path.Combine(Path.GetFullPath(sessionDirectory), "processing");
        var candidates = new List<Candidate>();
        var documents = new List<TranscriptDocument>();
        var chunkRoot = Path.Combine(root, "live-chunks");
        foreach (var sourceDirectory in Directory.GetDirectories(chunkRoot)
                     .Where(path => Path.GetFileName(path) == "system_audio" || Path.GetFileName(path).StartsWith("microphone", StringComparison.Ordinal)))
        {
            var source = Path.GetFileName(sourceDirectory);
            var logicalSource = source.StartsWith("microphone", StringComparison.Ordinal) ? "microphone" : source;
            var manifestPath = Path.Combine(sourceDirectory, "chunks.json");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException($"Live chunk manifest missing for {source}.", manifestPath);
            var manifest = JsonSerializer.Deserialize<IncrementalAudioChunkManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidDataException($"Live chunk manifest is empty for {source}.");
            foreach (var chunk in manifest.Chunks.OrderBy(item => item.Index))
            {
                var transcriptPath = Path.Combine(root, "live-transcripts", source, $"chunk_{chunk.Index:D6}.json");
                var document = TranscriptDocumentStore.Load(transcriptPath);
                documents.Add(document);
                foreach (var segment in document.Segments)
                {
                    var localStart = Math.Clamp(segment.Start, 0, chunk.DurationSeconds);
                    var localEnd = Math.Clamp(segment.End, localStart, chunk.DurationSeconds);
                    if (localEnd <= localStart || string.IsNullOrWhiteSpace(segment.Text)) continue;
                    candidates.Add(new(segment with
                    {
                        Start = chunk.StartSeconds + localStart,
                        End = chunk.StartSeconds + localEnd,
                        Source = logicalSource,
                        Speaker = logicalSource == "microphone" ? "You" : null,
                        SpeakerAssignment = logicalSource == "microphone" ? "known-source" : "not-run",
                        SpeakerOverlapRatio = logicalSource == "microphone" ? 1 : 0
                    }, chunk.Index));
                }
            }
        }
        if (documents.Count == 0) throw new InvalidDataException("No live chunk transcripts were found.");

        var reconciled = new List<TranscriptSegment>();
        foreach (var sourceGroup in candidates.GroupBy(item => item.Segment.Source))
        {
            Candidate? previous = null;
            foreach (var candidate in sourceGroup.OrderBy(item => item.Segment.Start).ThenBy(item => item.Segment.End))
            {
                var current = candidate;
                if (previous is not null && current.ChunkIndex > previous.ChunkIndex)
                {
                    var trimmed = TrimBoundaryOverlap(previous.Segment, current.Segment);
                    if (trimmed is null) continue;
                    current = current with { Segment = trimmed };
                }
                reconciled.Add(current.Segment);
                previous = current;
            }
        }

        var first = documents[0];
        return first with
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DetectedLanguages = documents.SelectMany(item => item.DetectedLanguages ?? new Dictionary<string, string?>())
                .GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.Select(item => item.Value).FirstOrDefault(value => value is not null)),
            Segments = reconciled.OrderBy(item => item.Start).ThenBy(item => item.End).ToArray(),
            DiarizationEnabled = false,
            SpeakerCount = 0,
            ProcessingDurationMilliseconds = documents.Sum(item => item.ProcessingDurationMilliseconds ?? 0)
        };
    }

    private static TranscriptSegment? TrimBoundaryOverlap(TranscriptSegment previous, TranscriptSegment current)
    {
        if (current.Start - previous.End > 3) return current;
        var previousWords = Words(previous.Text);
        var currentWords = Words(current.Text);
        if (previousWords.Length == 0 || currentWords.Length == 0) return current;
        var maximum = Math.Min(12, Math.Min(previousWords.Length, currentWords.Length));
        var overlap = 0;
        for (var count = maximum; count >= 3; count--)
        {
            if (previousWords[^count..].SequenceEqual(currentWords[..count], StringComparer.OrdinalIgnoreCase))
            {
                overlap = count;
                break;
            }
        }
        if (overlap == 0 && previousWords.SequenceEqual(currentWords, StringComparer.OrdinalIgnoreCase)) return null;
        if (overlap == 0) return current;
        if (overlap >= currentWords.Length) return null;
        var fraction = overlap / (double)currentWords.Length;
        return current with
        {
            Start = Math.Min(current.End, current.Start + (current.End - current.Start) * fraction),
            Text = string.Join(' ', currentWords[overlap..])
        };
    }

    private static string[] Words(string text) => WordPattern().Matches(text)
        .Select(match => match.Value).ToArray();

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordPattern();
}
