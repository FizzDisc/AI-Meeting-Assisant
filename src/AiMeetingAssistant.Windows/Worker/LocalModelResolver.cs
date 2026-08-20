namespace AiMeetingAssistant.Windows.Worker;

public sealed record LocalSpeechModelDefinition(string Id, string DisplayName, string DirectoryName,
    string Quality, string DownloadSize, string HardwareGuidance);

public static class LocalModelResolver
{
    public const string OverrideVariable = "AI_MEETING_ASSISTANT_MODEL";
    public const string DefaultSpeechModelId = "tiny";
    public static IReadOnlyList<LocalSpeechModelDefinition> SpeechModels { get; } =
    [
        new("tiny", "Whisper Tiny", "faster-whisper-tiny", "Basic", "~75 MB", "CPU friendly · fastest, lowest accuracy"),
        new("small", "Whisper Small", "faster-whisper-small", "Good", "~500 MB", "CPU usable · noticeably slower, better accuracy"),
        new("medium", "Whisper Medium", "faster-whisper-medium", "High", "~1.5 GB", "GPU recommended · slow on CPU, strongest option in this catalog")
    ];

    public static string? ResolveDevelopmentModel(string? baseDirectory = null)
        => ResolveSpeechModel(DefaultSpeechModelId, baseDirectory);

    public static string? ResolveSpeechModel(string? modelId, string? baseDirectory = null)
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);
        var model = GetSpeechModel(modelId);
        return ResolveModel(model.DirectoryName, null, baseDirectory);
    }

    public static LocalSpeechModelDefinition GetSpeechModel(string? modelId) =>
        SpeechModels.SingleOrDefault(model => string.Equals(model.Id, modelId, StringComparison.Ordinal))
        ?? SpeechModels.Single(model => model.Id == DefaultSpeechModelId);

    public static bool IsSpeechModelInstalled(string modelId, string? baseDirectory = null) =>
        ResolveModel(GetSpeechModel(modelId).DirectoryName, null, baseDirectory) is not null;

    public static string? ResolveDiarizationModel(string? baseDirectory = null)
        => ResolveModel("speaker-diarization-community-1", null, baseDirectory);

    public static string? ResolveOpenVinoSpeechModel(string? modelId, string? baseDirectory = null) =>
        modelId == "small" ? ResolveModel("openvino-whisper-small-fp16", null, baseDirectory) : null;

    public static string? ResolveOpenVinoRuntime(string? baseDirectory = null)
    {
        foreach (var start in new[] { baseDirectory ?? AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct())
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "worker", ".openvino-spike");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "openvino_genai", "__init__.py")))
                    return candidate;
            }
        return null;
    }

    public static string? ResolveTorchXpuRuntime(string? baseDirectory = null)
    {
        foreach (var start in new[] { baseDirectory ?? AppContext.BaseDirectory, Environment.CurrentDirectory }.Distinct())
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "worker", ".torch-xpu-spike");
                if (File.Exists(Path.Combine(candidate, "torch", "lib", "c10_xpu.dll")) &&
                    Directory.Exists(Path.Combine(candidate, "Library", "bin")))
                    return candidate;
            }
        return null;
    }

    public static string? ResolveSileroVad()
    {
        var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "torch", "hub", "snakers4_silero-vad_master");
        return Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "hubconf.py")) ? candidate : null;
    }

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
