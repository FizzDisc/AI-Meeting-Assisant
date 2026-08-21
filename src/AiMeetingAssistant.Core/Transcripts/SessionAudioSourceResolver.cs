using System.Text.Json;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Core.Transcripts;

public sealed record SessionAudioSource(string Kind, string Path, string Format, bool UsesArchive);

public static class SessionAudioSourceResolver
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] RequiredKinds = ["microphone", "system_audio"];

    public static IReadOnlyList<SessionAudioSource> Resolve(string sessionDirectory)
    {
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        var manifestPath = Path.Combine(session, "manifest.json");
        if (!File.Exists(manifestPath)) return [];
        CaptureSessionManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<CaptureSessionManifest>(File.ReadAllText(manifestPath), Options); }
        catch (JsonException) { return []; }
        if (manifest is null || !string.Equals(manifest.Status, "completed", StringComparison.OrdinalIgnoreCase)) return [];

        var result = new List<SessionAudioSource>();
        foreach (var kind in RequiredKinds)
        {
            var streams = manifest.Streams.Where(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (streams.Length != 1) return [];
            var wave = ResolveInsideSession(session, streams[0].RelativePath);
            if (wave is null) return [];
            var archive = Path.ChangeExtension(wave, ".flac");
            if (IsFlac(archive)) result.Add(new(kind, archive, "FLAC", true));
            else if (IsUsable(wave)) result.Add(new(kind, wave, "WAV", false));
            else return [];
        }
        return result;
    }

    private static string? ResolveInsideSession(string session, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var full = Path.GetFullPath(Path.Combine(session, relativePath));
        return full.StartsWith(session + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool IsUsable(string path) => File.Exists(path) && new FileInfo(path).Length > 0;
    private static bool IsFlac(string path)
    {
        if (!IsUsable(path)) return false;
        try
        {
            Span<byte> marker = stackalloc byte[4];
            using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return input.Read(marker) == marker.Length && marker.SequenceEqual("fLaC"u8);
        }
        catch (IOException) { return false; }
    }
}
