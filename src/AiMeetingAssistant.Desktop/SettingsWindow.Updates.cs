using System.Diagnostics;
using System.Windows;

namespace AiMeetingAssistant.Desktop;

public partial class SettingsWindow
{
    private void InitializeUpdates()
    {
        AutomaticUpdatesCheckBox.IsChecked = _original.CheckForUpdatesOnStartup;
        UpdateVersionText.Text = $"Installed: {AppVersion.Display}";
        ShowUpdateResult();
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking GitHub for updates...";
        try
        {
            await AppUpdates.CheckAsync();
            ShowUpdateResult(checkedNow: true);
        }
        catch (OperationCanceledException) { UpdateStatusText.Text = "Update check timed out. Please try again."; }
        catch (Exception) { UpdateStatusText.Text = "Could not check for updates. Check your connection or try again later (GitHub may limit requests)."; }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }

    private void ShowUpdateResult(bool checkedNow = false)
    {
        var release = AppUpdates.Latest;
        var newer = release?.IsNewerThan(AppUpdates.Current) == true;
        UpdateStatusText.Text = release is null
            ? checkedNow ? "No stable release is currently available." : "Check for a newer version when you are ready."
            : newer ? $"Version {release.Version.ToString(3)} is available."
            : "You are up to date (no newer stable release).";
        UpdateNotesText.Text = release?.Notes ?? "";
        DownloadUpdateButton.Visibility = newer && release!.InstallerUrl is not null ? Visibility.Visible : Visibility.Collapsed;
        ReleasePageButton.Visibility = release is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDownloadUpdate(object sender, RoutedEventArgs e) => OpenUpdateLink(AppUpdates.Latest?.InstallerUrl);
    private void OnOpenReleasePage(object sender, RoutedEventArgs e) => OpenUpdateLink(AppUpdates.Latest?.ReleaseUrl);
    private void OpenUpdateLink(Uri? uri)
    {
        if (uri is null) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { UpdateStatusText.Text = "Could not open your browser. Visit the project's GitHub Releases page."; }
    }
}
