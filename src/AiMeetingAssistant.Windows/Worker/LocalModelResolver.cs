namespace AiMeetingAssistant.Windows.Worker;

public static class LocalModelResolver
{
    public const string OverrideVariable = "AI_MEETING_ASSISTANT_MODEL";

    public static string? ResolveDevelopmentModel(string? baseDirectory = null)
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        foreach (var start in new[] { baseDirectory ?? AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct())
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "worker", "models", "faster-whisper-tiny");
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
        }
        return null;
    }
}
