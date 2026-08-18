using System.Windows;
using AiMeetingAssistant.Desktop.ViewModels;

namespace AiMeetingAssistant.Desktop;

public partial class MeetingLibraryWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MeetingLibraryWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnOpenSelectedTranscript(object sender, RoutedEventArgs e)
    {
        var path = _viewModel.SelectedMeetingSession?.TranscriptPath;
        if (path is null) return;
        try { new TranscriptWindow(path) { Owner = this }.ShowDialog(); }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not open transcript: {exception.Message}", "Transcript",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnDeleteSelectedRecording(object sender, RoutedEventArgs e)
    {
        var session = _viewModel.SelectedMeetingSession;
        if (session is null) return;
        var confirmation = MessageBox.Show(this,
            $"Delete the recording from {session.StartedLabel}?\n\nThis permanently removes its video, audio, transcript and session metadata.",
            "Delete recording", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;
        try { await _viewModel.DeleteSelectedMeetingAsync(); }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not delete recording: {exception.Message}", "Delete recording",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
