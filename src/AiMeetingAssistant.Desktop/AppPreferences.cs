using System.IO;
using System.Text.Json;

namespace AiMeetingAssistant.Desktop;

internal static class AppPreferences
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI Meeting Assistant");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public static bool LoadScreenCaptureEnabled()
    {
        try
        {
            if (!File.Exists(FilePath)) return true;
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath))?.ScreenCaptureEnabled ?? true;
        }
        catch { return true; }
    }

    public static void SaveScreenCaptureEnabled(bool enabled)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new Preferences(enabled), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, FilePath, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private sealed record Preferences(bool ScreenCaptureEnabled);
}
