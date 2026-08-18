namespace AiMeetingAssistant.Core.Capture;

public static class CaptureAlignmentAnalyzer
{
    public const double DefaultToleranceMilliseconds = 100;

    public static CaptureSessionManifest Analyze(string sessionDirectory, CaptureSessionManifest manifest, double toleranceMilliseconds = DefaultToleranceMilliseconds)
    {
        try
        {
            var enriched = manifest.Streams.Select(stream =>
            {
                var duration = MediaDurationReader.ReadMilliseconds(Path.Combine(sessionDirectory, stream.RelativePath));
                var end = stream.StartOffsetMilliseconds + duration;
                return stream with { MediaDurationMilliseconds = duration, ExpectedEndOffsetMilliseconds = end, SessionEndDifferenceMilliseconds = manifest.DurationMilliseconds is null ? null : end - manifest.DurationMilliseconds.Value };
            }).ToArray();
            var ends = enriched.Select(stream => stream.ExpectedEndOffsetMilliseconds!.Value).ToArray();
            var spread = ends.Length == 0 ? 0 : ends.Max() - ends.Min();
            var status = spread <= toleranceMilliseconds ? "aligned" : "warning";
            return manifest with { Streams = enriched, Alignment = new(status, toleranceMilliseconds, spread, status == "aligned" ? null : "Stream end spread exceeds tolerance.") };
        }
        catch (Exception exception)
        {
            return manifest with { Alignment = new("unavailable", toleranceMilliseconds, null, exception.Message) };
        }
    }
}
