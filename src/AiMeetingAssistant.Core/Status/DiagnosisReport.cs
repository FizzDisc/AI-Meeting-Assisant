using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiMeetingAssistant.Core.Status;

public enum DiagnosisArea { Setup, ModelSetup, CaptureOrProcessing, Updates, Settings }
public enum RuntimeCheckState { NotChecked, Ready, NeedsSetup }
public sealed record DiagnosisEvent(DateTimeOffset TimestampUtc, DiagnosisArea Area, ProblemCode Code);

public sealed class DiagnosisHistory
{
    private readonly object _gate = new();
    private readonly Queue<DiagnosisEvent> _events = new();
    public void Add(DiagnosisArea area, ProblemCode code)
    {
        lock (_gate)
        {
            if (_events.Count == 50) _events.Dequeue();
            _events.Enqueue(new(DateTimeOffset.UtcNow, area, code));
        }
    }
    public DiagnosisEvent[] Snapshot() { lock (_gate) return _events.ToArray(); }
}

// An allowlist of typed fields: no free-form logs, paths, device names or worker responses.
public sealed record DiagnosisReport(
    Version AppVersion, Version WindowsVersion, bool Is64BitProcess,
    bool PythonFileFound, bool ManagedFfmpegFileFound, bool CustomPythonConfigured,
    RuntimeCheckState LastRuntimeCheck, bool TinyModelFound, bool SmallModelFound, bool MediumModelFound,
    bool CustomModelConfigured, bool StartupUpdateCheckEnabled, DiagnosisEvent[] RecentEvents)
{
    public string ToJson() => JsonSerializer.Serialize(new
    {
        SchemaVersion = 1,
        GeneratedUtc = DateTimeOffset.UtcNow,
        Scope = "Current session only. File presence does not prove runtime readiness. No raw logs or personal paths included.",
        AppVersion = AppVersion.ToString(), WindowsVersion = WindowsVersion.ToString(), Is64BitProcess,
        PythonFileFound, ManagedFfmpegFileFound, CustomPythonConfigured, LastRuntimeCheck,
        TinyModelFound, SmallModelFound, MediumModelFound, CustomModelConfigured, StartupUpdateCheckEnabled,
        RecentEvents
    }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });
}
