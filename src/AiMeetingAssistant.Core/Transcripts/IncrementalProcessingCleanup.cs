namespace AiMeetingAssistant.Core.Transcripts;

public sealed record IncrementalProcessingCleanupResult(long ReclaimedBytes, int DeletedFiles,
    IReadOnlyList<string> Warnings);
public sealed record IncrementalProcessingCleanupPreview(long ReclaimableBytes, int Files, int Sessions);

public static class IncrementalProcessingCleanup
{
    public static IncrementalProcessingCleanupPreview PreviewLibrary(string libraryDirectory)
    {
        var root=Path.GetFullPath(libraryDirectory);if(!Directory.Exists(root))return new(0,0,0);long bytes=0;var files=0;var sessions=0;
        foreach(var session in Directory.EnumerateDirectories(root,"session_*",SearchOption.TopDirectoryOnly)){var targets=EnumerateTargets(session).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();if(targets.Length==0)continue;sessions++;files+=targets.Length;foreach(var file in targets)try{bytes+=new FileInfo(file).Length;}catch{}}
        return new(bytes,files,sessions);
    }
    public static IncrementalProcessingCleanupResult CleanLibrary(string libraryDirectory){var root=Path.GetFullPath(libraryDirectory);if(!Directory.Exists(root))return new(0,0,[]);long bytes=0;var files=0;var warnings=new List<string>();foreach(var session in Directory.EnumerateDirectories(root,"session_*",SearchOption.TopDirectoryOnly)){var result=AfterSuccessfulFinalization(session);bytes+=result.ReclaimedBytes;files+=result.DeletedFiles;warnings.AddRange(result.Warnings);}return new(bytes,files,warnings);}
    public static IncrementalProcessingCleanupResult AfterSuccessfulFinalization(string sessionDirectory)
    {
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        if (!Path.GetFileName(session).StartsWith("session_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Incremental cleanup is restricted to a session_* workspace.");
        var processing = Path.Combine(session, "processing");
        if (!Directory.Exists(processing)) return new(0, 0, []);

        long bytes = 0;
        var files = 0;
        var warnings = new List<string>();
        DeleteDirectory(Path.Combine(processing, "live-chunks"), ref bytes, ref files, warnings);
        DeleteDirectory(Path.Combine(processing, "live-transcripts", ".batches"), ref bytes, ref files, warnings);
        DeleteDirectory(Path.Combine(processing, "preliminary"), ref bytes, ref files, warnings);
        DeleteFile(Path.Combine(processing, "normalized_incremental_system_audio.wav"), ref bytes, ref files, warnings);

        var liveTranscripts = Path.Combine(processing, "live-transcripts");
        if (Directory.Exists(liveTranscripts))
        {
            foreach (var path in Directory.EnumerateFiles(liveTranscripts, "*", SearchOption.AllDirectories).ToArray())
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("normalized_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("transcription-", StringComparison.OrdinalIgnoreCase)
                    && (name.EndsWith(".request.json", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".status.json", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".worker.log", StringComparison.OrdinalIgnoreCase)))
                    DeleteFile(path, ref bytes, ref files, warnings);
            }
        }
        return new(bytes, files, warnings);
    }

    private static IEnumerable<string> EnumerateTargets(string sessionDirectory)
    {
        var session=Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));if(!Path.GetFileName(session).StartsWith("session_",StringComparison.OrdinalIgnoreCase))yield break;var processing=Path.Combine(session,"processing");if(!Directory.Exists(processing))yield break;
        foreach(var directory in new[]{Path.Combine(processing,"live-chunks"),Path.Combine(processing,"live-transcripts",".batches"),Path.Combine(processing,"preliminary")})if(Directory.Exists(directory))foreach(var file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories))yield return file;
        var normalized=Path.Combine(processing,"normalized_incremental_system_audio.wav");if(File.Exists(normalized))yield return normalized;var live=Path.Combine(processing,"live-transcripts");if(!Directory.Exists(live))yield break;foreach(var path in Directory.EnumerateFiles(live,"*",SearchOption.AllDirectories)){var name=Path.GetFileName(path);if(name.StartsWith("normalized_",StringComparison.OrdinalIgnoreCase)&&name.EndsWith(".wav",StringComparison.OrdinalIgnoreCase)||name.StartsWith("transcription-",StringComparison.OrdinalIgnoreCase)&&(name.EndsWith(".request.json",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".status.json",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".worker.log",StringComparison.OrdinalIgnoreCase)))yield return path;}
    }

    private static void DeleteDirectory(string path, ref long bytes, ref int files, List<string> warnings)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            var evidence = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file => new FileInfo(file).Length).ToArray();
            Directory.Delete(path, recursive: true);
            bytes += evidence.Sum();
            files += evidence.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not remove {Path.GetFileName(path)}: {exception.Message}");
        }
    }

    private static void DeleteFile(string path, ref long bytes, ref int files, List<string> warnings)
    {
        if (!File.Exists(path)) return;
        try
        {
            var length = new FileInfo(path).Length;
            File.Delete(path);
            bytes += length;
            files++;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not remove {Path.GetFileName(path)}: {exception.Message}");
        }
    }
}
