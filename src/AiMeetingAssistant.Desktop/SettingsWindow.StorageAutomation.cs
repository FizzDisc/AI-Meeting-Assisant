using System.Windows;

namespace AiMeetingAssistant.Desktop;

public partial class SettingsWindow
{
    private bool _storageAutomationInitialized;
    private void OnStorageAutomationLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_storageAutomationInitialized) return;
        _storageAutomationInitialized = true;
        var settings = AppPreferences.Load();
        AutomaticFlacCheckBox.IsChecked = settings.AutomaticFlacArchival;
        AutomaticWavRemovalCheckBox.IsChecked = settings.AutomaticProvenWavRemoval;
        Closed += OnStorageAutomationClosed;
    }

    private void OnStorageAutomationClosed(object? sender, EventArgs eventArgs)
    {
        if (DialogResult != true) return;
        var settings = AppPreferences.Load();
        AppPreferences.Save(settings with
        {
            AutomaticFlacArchival = AutomaticFlacCheckBox.IsChecked == true,
            AutomaticProvenWavRemoval = AutomaticWavRemovalCheckBox.IsChecked == true
        });
    }
}
