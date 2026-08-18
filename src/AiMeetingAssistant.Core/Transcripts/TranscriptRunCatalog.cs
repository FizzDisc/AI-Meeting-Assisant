namespace AiMeetingAssistant.Core.Transcripts;

public sealed record TranscriptRunInfo(string Path, DateTimeOffset CreatedAtUtc, string ModelId,
    string Device, string ComputeType, long? ProcessingDurationMilliseconds, int SegmentCount)
{
    public string CreatedLabel => CreatedAtUtc.ToLocalTime().ToString("g");
    public string ModelLabel => ModelId;
    public string ComputeLabel => $"{Device}/{ComputeType}";
    public string ProcessingDurationLabel => ProcessingDurationMilliseconds is long value
        ? TimeSpan.FromMilliseconds(value).ToString(@"hh\:mm\:ss") : "unknown";
}

public static class TranscriptRunCatalog
{
    public static IReadOnlyList<TranscriptRunInfo> Discover(string sessionDirectory)
    {
        var processing = Path.Combine(sessionDirectory, "processing");
        if (!Directory.Exists(processing)) return [];
        var versioned = Directory.EnumerateFiles(processing, "transcript_*.json", SearchOption.TopDirectoryOnly);
        var canonical = Path.Combine(processing, "transcript.json");
        var candidates = versioned.Concat(File.Exists(canonical) ? [canonical] : []).ToArray();
        var results = new List<TranscriptRunInfo>();
        foreach (var path in candidates)
        {
            try
            {
                var document = TranscriptDocumentStore.Load(path);
                results.Add(new(path, document.CreatedAtUtc, document.ModelId ?? "legacy/unknown",
                    document.Device ?? "unknown", document.ComputeType ?? "unknown",
                    document.ProcessingDurationMilliseconds, document.Segments.Count));
            }
            catch { /* One corrupt run must not hide the remaining history. */ }
        }
        return results.GroupBy(run => (run.CreatedAtUtc, run.ModelId, run.ProcessingDurationMilliseconds, run.SegmentCount))
            .Select(group => group.First()).OrderByDescending(run => run.CreatedAtUtc).ToArray();
    }
}
