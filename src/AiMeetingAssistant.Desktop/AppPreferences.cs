using System.IO;
using System.Text.Json;
namespace AiMeetingAssistant.Desktop;
internal sealed record AppSettings(int SchemaVersion, bool ScreenCaptureEnabled, string CaptureDirectory, string? ModelDirectory,
    string ComputePreference, string SpeechModelId = "tiny", double SystemAudioGain = 1.0, double MicrophoneGain = 1.0)
{ public static AppSettings Defaults => new(1, true, Path.GetFullPath("artifacts/captures"), null, "automatic", "tiny", 1.0, 1.0); }
internal static class AppPreferences
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI Meeting Assistant");
    internal static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static AppSettings Load() { try { if (!File.Exists(FilePath)) return AppSettings.Defaults; var v=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath),Options)??AppSettings.Defaults; return v with { SchemaVersion=1, CaptureDirectory=Path.GetFullPath(string.IsNullOrWhiteSpace(v.CaptureDirectory)?AppSettings.Defaults.CaptureDirectory:v.CaptureDirectory), ComputePreference=Normalize(v.ComputePreference), SpeechModelId=NormalizeModel(v.SpeechModelId), SystemAudioGain=NormalizeGain(v.SystemAudioGain), MicrophoneGain=NormalizeGain(v.MicrophoneGain)}; } catch { return AppSettings.Defaults; } }
    public static bool LoadScreenCaptureEnabled()=>Load().ScreenCaptureEnabled;
    public static void SaveScreenCaptureEnabled(bool enabled)=>Save(Load() with { ScreenCaptureEnabled=enabled });
    public static void Save(AppSettings value)
    {
        if(value.SchemaVersion!=1) throw new InvalidDataException("Unsupported settings schema.");
        var capture=Path.GetFullPath(value.CaptureDirectory); Directory.CreateDirectory(capture);
        if(!string.IsNullOrWhiteSpace(value.ModelDirectory)&&!Directory.Exists(value.ModelDirectory)) throw new DirectoryNotFoundException("The selected model directory does not exist.");
        value=value with { CaptureDirectory=capture, ModelDirectory=string.IsNullOrWhiteSpace(value.ModelDirectory)?null:Path.GetFullPath(value.ModelDirectory), ComputePreference=Normalize(value.ComputePreference), SpeechModelId=NormalizeModel(value.SpeechModelId), SystemAudioGain=NormalizeGain(value.SystemAudioGain), MicrophoneGain=NormalizeGain(value.MicrophoneGain)};
        Directory.CreateDirectory(DirectoryPath); var temp=$"{FilePath}.{Guid.NewGuid():N}.tmp";
        try { File.WriteAllText(temp,JsonSerializer.Serialize(value,Options)); File.Move(temp,FilePath,true); } finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    private static string Normalize(string? value)=>value is "automatic" or "cpu-only" or "prefer-cuda" or "intel-gpu"?value:"automatic";
    private static string NormalizeModel(string? value)=>value is "tiny" or "small" or "medium"?value:"tiny";
    private static double NormalizeGain(double value)=>double.IsFinite(value)&&value>=0.25&&value<=1.5?value:1.0;
}
