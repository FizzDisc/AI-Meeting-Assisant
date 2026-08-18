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
        _viewModel = new(TranscriptDocumentStore.Load(transcriptPath), Path.GetFullPath(transcriptPath));
        DataContext = _viewModel;
    }

    private void OnExportMarkdown(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDialog("Markdown document|*.md", ".md");
        if (dialog.ShowDialog(this) == true)
        {
            TranscriptDocumentStore.ExportMarkdownAtomic(dialog.FileName, _viewModel.Document);
            MessageBox.Show(this, "Markdown export completed.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
