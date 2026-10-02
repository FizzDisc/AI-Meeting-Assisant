using System.Windows;

namespace AiMeetingAssistant.Desktop;

public partial class MainWindow
{
    private void InitializeUpdates()
    {
        AppUpdates.Changed += RefreshUpdateNotice;
        Closed += (_, _) => AppUpdates.Changed -= RefreshUpdateNotice;
        RefreshUpdateNotice();
    }

    private async Task CheckStartupUpdatesAsync()
    {
        if (!AppPreferences.Load().CheckForUpdatesOnStartup) return;
        try { await AppUpdates.CheckAsync(); }
        catch (Exception) { /* Offline startup stays quiet; Settings offers a manual retry. */ }
    }

    private void RefreshUpdateNotice()
    {
        var release = AppUpdates.Latest;
        UpdateNoticeButton.Visibility = release?.IsNewerThan(AppUpdates.Current) == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateNoticeButton.Content = release is null ? "" : $"Update {release.Version.ToString(3)} available";
    }
}
