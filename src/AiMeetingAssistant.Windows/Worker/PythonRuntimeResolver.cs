namespace AiMeetingAssistant.Windows.Worker;

public static class PythonRuntimeResolver
{
    public const string OverrideVariable = "AI_MEETING_ASSISTANT_PYTHON";

    public static string Resolve(string? baseDirectory = null)
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var directory = new DirectoryInfo(baseDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "worker", ".venv", "Scripts", "python.exe");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return "python";
    }
}
