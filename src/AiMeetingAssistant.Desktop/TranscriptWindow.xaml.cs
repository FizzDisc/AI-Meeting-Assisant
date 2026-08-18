using System.IO;
using System.Windows;
using AiMeetingAssistant.Core.Transcripts;
using AiMeetingAssistant.Desktop.ViewModels;
using Microsoft.Win32;

namespace AiMeetingAssistant.Desktop;

public partial class TranscriptWindow : Window
{
    private readonly TranscriptWindowViewModel _viewModel;

    public TranscriptWindow(string transcriptPath)
    {
        InitializeComponent();
        var fullPath = Path.GetFullPath(transcriptPath);
        _viewModel = new(TranscriptDocumentStore.Load(fullPath), fullPath, SpeakerNameStore.LoadForTranscript(fullPath));
        DataContext = _viewModel;
    }

    private void OnExportMarkdown(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDialog("Markdown document|*.md", ".md");
        if (dialog.ShowDialog(this) == true)
        {
            TranscriptDocumentStore.ExportMarkdownAtomic(dialog.FileName, _viewModel.Document, _viewModel.SpeakerNames);
            MessageBox.Show(this, "Markdown export completed.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnManageSpeakers(object sender, RoutedEventArgs e)
    {
        var speakerIds = _viewModel.Document.Segments
            .Select(segment => segment.Speaker)
            .Where(speaker => speaker == "You" || (speaker?.StartsWith("SPEAKER_", StringComparison.Ordinal) ?? false))
            .Cast<string>().Distinct(StringComparer.Ordinal).OrderBy(speaker => speaker == "You" ? "" : speaker).ToArray();
        var dialog = new SpeakerNamesWindow(speakerIds, _viewModel.SpeakerNames) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        SpeakerNameStore.SaveForTranscript(_viewModel.SourcePath, dialog.SpeakerNames);
        _viewModel.UpdateSpeakerNames(SpeakerNameStore.LoadForTranscript(_viewModel.SourcePath));
    }

    private void OnExportJson(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDialog("JSON document|*.json", ".json");
        if (dialog.ShowDialog(this) == true)
        {
            TranscriptDocumentStore.ExportJsonAtomic(dialog.FileName, _viewModel.Document);
            MessageBox.Show(this, "JSON export completed.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private SaveFileDialog CreateDialog(string filter, string extension) => new()
    {
        Title = "Export meeting transcript",
        Filter = filter,
        DefaultExt = extension,
        AddExtension = true,
        FileName = $"transcript_{DateTime.Now:yyyyMMdd_HHmmss}{extension}",
        InitialDirectory = Path.GetDirectoryName(_viewModel.SourcePath)
    };
}
