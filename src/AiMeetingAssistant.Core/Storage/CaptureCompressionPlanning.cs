using System.Text.Json;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Core.Storage;

public sealed record CaptureCompressionCandidate(
    string StreamKind,
    string SourcePath,
    string ProposedArchivePath,
    long SourceBytes,
    long EstimatedArchiveBytes,
    long EstimatedSavingsBytes)
{
    public string SourceLabel => StorageInventory.Format(SourceBytes);
    public string EstimatedSavingsLabel => StorageInventory.Format(EstimatedSavingsBytes);
}

public sealed record ProtectedCaptureEvidence(string RelativePath, string Reason);

public sealed record SessionCompressionPlan(
    string SessionName,
    bool ManifestValidated,
    long EstimatedSavingsBytes,
    IReadOnlyList<CaptureCompressionCandidate> Candidates,
    IReadOnlyList<ProtectedCaptureEvidence> ProtectedEvidence)
{
    public string EstimatedSavingsLabel => StorageInventory.Format(EstimatedSavingsBytes);
}

public sealed record CaptureCompressionPlanReport(
    string Root,
    long CandidateBytes,
    long EstimatedSavingsBytes,
    IReadOnlyList<SessionCompressionPlan> Sessions,
    IReadOnlyList<string> RequiredVerification)
{
    public int CandidateCount => Sessions.Sum(session => session.Candidates.Count);
    public string EstimatedSavingsLabel => StorageInventory.Format(EstimatedSavingsBytes);
}

/// <summary>
/// Builds a read-only archival plan. It deliberately performs no transcoding,
/// replacement or deletion. Execution belongs to a later adapter that can verify
/// the encoded media before retaining or removing any capture master.
/// </summary>
public static class CaptureCompressionPlanner
{
    // PCM meeting audio commonly compresses more than this. Keeping the estimate
    // conservative avoids promising storage that a particular recording cannot save.
    private const double EstimatedFlacRatio = 0.60;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static CaptureCompressionPlanReport AnalyzeLibrary(string libraryDirectory)
    {
        var root = Path.GetFullPath(libraryDirectory);
        if (!Directory.Exists(root)) return Empty(root);

        var sessions = Directory.EnumerateDirectories(root, "session_*", SearchOption.TopDirectoryOnly)
            .Select(AnalyzeSession)
            .OrderByDescending(session => session.EstimatedSavingsBytes)
            .ToArray();
        var candidates = sessions.SelectMany(session => session.Candidates).ToArray();
        return new(root, candidates.Sum(candidate => candidate.SourceBytes),
            candidates.Sum(candidate => candidate.EstimatedSavingsBytes), sessions, VerificationRules);
    }

    public static SessionCompressionPlan AnalyzeSession(string sessionDirectory)
    {
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        if (!Path.GetFileName(session).StartsWith("session_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Compression analysis is restricted to a session_* workspace.");

        var manifestPath = Path.Combine(session, "manifest.json");
        if (!TryReadCompletedManifest(manifestPath, out var manifest, out var manifestFailure))
            return new(Path.GetFileName(session), false, 0, [], [new("manifest.json", manifestFailure)]);

        var candidates = new List<CaptureCompressionCandidate>();
        var protectedEvidence = new List<ProtectedCaptureEvidence>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stream in manifest!.Streams)
        {
            var relativePath = stream.RelativePath ?? string.Empty;
            if (!TryResolveWithinSession(session, relativePath, out var sourcePath))
            {
                protectedEvidence.Add(new(relativePath, "Manifest path leaves the session workspace."));
                continue;
            }
            if (!seen.Add(sourcePath)) continue;
            if (!File.Exists(sourcePath))
            {
                protectedEvidence.Add(new(relativePath, "Manifest-referenced capture master is missing."));
                continue;
            }
            if (!Path.GetExtension(sourcePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                protectedEvidence.Add(new(relativePath, "Already-compressed video or unsupported master remains unchanged."));
                continue;
            }
            if (!HasPcmWaveSignature(sourcePath))
            {
                protectedEvidence.Add(new(relativePath, "WAV signature could not be validated."));
                continue;
            }

            var archivePath = Path.ChangeExtension(sourcePath, ".flac");
            if (File.Exists(archivePath))
            {
                protectedEvidence.Add(new(relativePath, "A proposed FLAC archive already exists; never overwrite it."));
                continue;
            }
            var sourceBytes = new FileInfo(sourcePath).Length;
            var estimatedArchiveBytes = (long)Math.Ceiling(sourceBytes * EstimatedFlacRatio);
            candidates.Add(new(stream.Kind, relativePath, Path.GetRelativePath(session, archivePath), sourceBytes,
                estimatedArchiveBytes, Math.Max(0, sourceBytes - estimatedArchiveBytes)));
        }

        return new(Path.GetFileName(session), true, candidates.Sum(candidate => candidate.EstimatedSavingsBytes),
            candidates, protectedEvidence);
    }

    private static bool TryReadCompletedManifest(string path, out CaptureSessionManifest? manifest, out string failure)
    {
        manifest = null;
        if (!File.Exists(path)) { failure = "Session has no manifest; capture evidence is protected."; return false; }
        try { manifest = JsonSerializer.Deserialize<CaptureSessionManifest>(File.ReadAllText(path), JsonOptions); }
        catch (JsonException) { failure = "Session manifest is invalid; capture evidence is protected."; return false; }
        if (manifest is null) { failure = "Session manifest is empty; capture evidence is protected."; return false; }
        if (!manifest.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            failure = $"Session status is '{manifest.Status}', not completed; capture evidence is protected.";
            return false;
        }
        failure = string.Empty;
        return true;
    }

    private static bool TryResolveWithinSession(string session, string relativePath, out string resolved)
    {
        resolved = Path.GetFullPath(Path.Combine(session, relativePath));
        return resolved.StartsWith(session + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && !Path.GetRelativePath(session, resolved).StartsWith("processing" + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPcmWaveSignature(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[12];
            using var stream = File.OpenRead(path);
            return stream.Read(header) == header.Length
                   && header[..4].SequenceEqual("RIFF"u8)
                   && header[8..].SequenceEqual("WAVE"u8);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static CaptureCompressionPlanReport Empty(string root) => new(root, 0, 0, [], VerificationRules);

    private static readonly string[] VerificationRules =
    [
        "Write the FLAC to a new sibling path; never overwrite a capture master.",
        "Finalize atomically and verify decoder readability, duration, sample rate and channel count.",
        "Prove retranscription accepts the archive before offering removal of the PCM master.",
        "Keep the original master when conversion, verification or manifest publication fails.",
        "Require an explicit user confirmation before any later master removal."
    ];
}
