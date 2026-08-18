using System.Text.Json;

namespace AiMeetingAssistant.Core.Capture;

public sealed record CaptureRecoveryReport(int ScannedSessions, int RecoveredSessions, IReadOnlyList<string> Issues);

public static class CaptureSessionRecovery
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public static CaptureRecoveryReport RecoverInterrupted(string baseDirectory)
    {
        if (!Directory.Exists(baseDirectory)) return new(0, 0, []);
        var scanned = 0; var recovered = 0; var issues = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(baseDirectory, "session_*", SearchOption.TopDirectoryOnly))
        {
            var path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path)) { issues.Add($"{Path.GetFileName(directory)} has no manifest.json."); continue; }
            scanned++;
            try
            {
                var manifest = JsonSerializer.Deserialize<CaptureSessionManifest>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Manifest is empty.");
                if (manifest.Status is not ("preparing" or "recording")) continue;
                var unusable = manifest.Streams.Where(stream =>
                {
                    var artifact = Path.Combine(directory, stream.RelativePath);
                    return !File.Exists(artifact) || new FileInfo(artifact).Length == 0;
                }).Select(stream => stream.Kind).ToArray();
                var detail = unusable.Length == 0 ? "Session ended without normal finalization; validate media before processing." : $"Session ended without normal finalization; missing or empty artifacts: {string.Join(", ", unusable)}.";
                CaptureSessionManifestStore.WriteAtomic(path, manifest with { Status = "interrupted", CompletedAtUtc = DateTimeOffset.UtcNow, Alignment = new("unavailable", CaptureAlignmentAnalyzer.DefaultToleranceMilliseconds, null, detail) });
                recovered++;
            }
            catch (Exception exception) { issues.Add($"{Path.GetFileName(directory)} could not be recovered: {exception.Message}"); }
        }
        return new(scanned, recovered, issues);
    }
}
