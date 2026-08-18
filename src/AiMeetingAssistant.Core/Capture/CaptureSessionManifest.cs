using System.Text.Json;

namespace AiMeetingAssistant.Core.Capture;

public sealed record CaptureSessionManifest(int SchemaVersion, string SessionId, string Status, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, double? DurationMilliseconds, CapturePlan Sources, IReadOnlyList<CaptureStreamManifest> Streams);
public sealed record CaptureStreamManifest(string Kind, string RelativePath, double StartOffsetMilliseconds);

public static class CaptureSessionManifestStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void WriteAtomic(string path, CaptureSessionManifest manifest)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, Options));
            File.Move(temporaryPath, fullPath, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
