namespace AiMeetingAssistant.Windows.Worker;

public static class LocalModelResolver
{
    public const string OverrideVariable = "AI_MEETING_ASSISTANT_MODEL";

    public static string? ResolveDevelopmentModel(string? baseDirectory = null)
        => ResolveModel("faster-whisper-tiny", OverrideVariable, baseDirectory);

    public static string? ResolveDiarizationModel(string? baseDirectory = null)
        => ResolveModel("speaker-diarization-community-1", null, baseDirectory);

    private static string? ResolveModel(string directoryName, string? overrideVariable, string? baseDirectory)
    {
        var configured = overrideVariable is null ? null : Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        foreach (var start in new[] { baseDirectory ?? AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct())
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "worker", "models", directoryName);
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
        }
        return null;
    }
}
