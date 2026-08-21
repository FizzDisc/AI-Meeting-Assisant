namespace AiMeetingAssistant.Core.Transcripts;

public static class IncrementalTranscriptBatchSplitter
{
    public static void Split(TranscriptDocument combined, IReadOnlyDictionary<string, string> outputPaths)
    {
        ArgumentNullException.ThrowIfNull(combined);
        ArgumentNullException.ThrowIfNull(outputPaths);
        if (outputPaths.Count < 2) throw new ArgumentException("A transcript batch requires at least two sources.", nameof(outputPaths));

        foreach (var (source, outputPath) in outputPaths)
        {
            var language = combined.DetectedLanguages is not null
                && combined.DetectedLanguages.TryGetValue(source, out var detected) ? detected : null;
            var document = combined with
            {
                Language = language,
                DetectedLanguages = new Dictionary<string, string?> { [source] = language },
                AudioEvidence = combined.AudioEvidence is not null && combined.AudioEvidence.TryGetValue(source, out var evidence)
                    ? new Dictionary<string, TranscriptAudioEvidence> { [source] = evidence }
                    : null,
                SkippedSources = combined.SkippedSources?.Contains(source, StringComparer.Ordinal) == true ? [source] : [],
                Segments = combined.Segments.Where(segment => segment.Source == source).ToArray(),
                ProcessingDurationMilliseconds = combined.ProcessingDurationMilliseconds is null
                    ? null
                    : combined.ProcessingDurationMilliseconds / outputPaths.Count
            };
            TranscriptDocumentStore.ExportJsonAtomic(outputPath, document);
        }
    }
}
