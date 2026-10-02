using System.IO;
using System.Windows;
using AiMeetingAssistant.Core.Status;
using AiMeetingAssistant.Windows.Worker;
using Microsoft.Win32;

namespace AiMeetingAssistant.Desktop;

internal static class AppDiagnosis
{
    private static readonly DiagnosisHistory History = new();
    public static RuntimeCheckState RuntimeState { get; set; }

    public static string Describe(Exception error, DiagnosisArea area)
    {
        var guidance = FailureGuidance.FromException(error);
        History.Add(area, guidance.Code);
        return guidance.Message;
    }

    public static void Record(string message, DiagnosisArea area) => History.Add(area, FailureGuidance.FromMessage(message).Code);

    public static void Export(Window owner)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export diagnosis report", Filter = "Diagnosis report (*.json)|*.json",
            FileName = $"AI-Meeting-Assistant-diagnosis-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            DefaultExt = ".json", AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            var settings = AppPreferences.Load();
            var report = new DiagnosisReport(AppUpdates.Current, Environment.OSVersion.Version, Environment.Is64BitProcess,
                File.Exists(PythonRuntimeResolver.Resolve()), File.Exists(UserAiPaths.Ffmpeg),
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PythonRuntimeResolver.OverrideVariable)), RuntimeState,
                LocalModelResolver.IsSpeechModelInstalled("tiny"), LocalModelResolver.IsSpeechModelInstalled("small"),
                LocalModelResolver.IsSpeechModelInstalled("medium"), !string.IsNullOrWhiteSpace(settings.ModelDirectory),
                settings.CheckForUpdatesOnStartup, History.Snapshot());
            File.WriteAllText(dialog.FileName, report.ToJson());
            MessageBox.Show(owner, "Diagnosis report saved. It contains version numbers, component presence and error categories from this session. No recordings, transcripts, access tokens, raw logs or personal paths are included. Nothing was uploaded.", "Diagnosis report", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(owner, Describe(error, DiagnosisArea.Settings), "Could not save report", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
