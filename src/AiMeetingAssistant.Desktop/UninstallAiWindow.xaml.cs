using System.Windows;
using AiMeetingAssistant.Windows.Worker;

namespace AiMeetingAssistant.Desktop;

public partial class UninstallAiWindow : Window
{
    private bool _busy;
    public UninstallAiWindow()
    {
        InitializeComponent();
        FolderHint.Text = "App-Datenordner: " + UserAiPaths.Root;
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
    }

    private async void Continue(object sender, RoutedEventArgs e)
    {
        var models = RemoveModels.IsChecked == true;
        var runtime = RemoveRuntime.IsChecked == true;
        if (!models && !runtime) { DialogResult = true; return; }
        _busy = true;
        ContinueButton.IsEnabled = RemoveModels.IsEnabled = RemoveRuntime.IsEnabled = false;
        Busy.Visibility = Visibility.Visible;
        Status.Text = "Die ausgewählten Komponenten werden entfernt. Das kann etwas dauern …";
        try
        {
            var captureDirectory = AppPreferences.Load().CaptureDirectory;
            var errors = await Task.Run(() => AiComponentCleanup.Remove(UserAiPaths.Root, models, runtime, captureDirectory));
            AppPreferences.RequireSetupAfterReinstall();
            if (errors.Count > 0)
                MessageBox.Show(this, "Einige Komponenten konnten nicht entfernt werden, etwa weil Dateien noch geöffnet sind. Die App kann trotzdem deinstalliert werden.\n\n" + string.Join("\n\n", errors),
                    "KI-Komponenten teilweise beibehalten", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "Die Bereinigung konnte nicht vollständig abgeschlossen werden. Die Deinstallation der App wird fortgesetzt.\n\n" + error.Message,
                "KI-Komponenten", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _busy = false; }
        DialogResult = true;
    }
}
