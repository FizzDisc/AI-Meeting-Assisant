using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiMeetingAssistant.Core.Transcripts;

namespace AiMeetingAssistant.Desktop.ViewModels;

public sealed class TranscriptWindowViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<TranscriptSegmentViewModel> _allSegments;
    private bool _showMicrophone = true;
    private bool _showSystemAudio = true;
    private IReadOnlyList<TranscriptSegmentViewModel> _segments;

    public TranscriptWindowViewModel(TranscriptDocument document, string sourcePath)
    {
        Document = document;
        SourcePath = sourcePath;
        _allSegments = document.Segments.Select(segment => new TranscriptSegmentViewModel(
            TranscriptDocumentStore.FormatTimestamp(segment.Start),
            TranscriptDocumentStore.FormatSpeaker(segment), TranscriptDocumentStore.FormatSource(segment.Source),
            segment.Source, segment.SpeakerAssignment, segment.Text)).ToArray();
        _segments = _allSegments;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public TranscriptDocument Document { get; }
    public string SourcePath { get; }
    public string Summary => $"{Document.Segments.Count} segments · {Document.SpeakerCount ?? 0} detected system speaker(s) · {Document.Language ?? "multiple/unknown"} · {Document.Device ?? "unknown"}/{Document.ComputeType ?? "unknown"}";
    public IReadOnlyList<TranscriptSegmentViewModel> Segments { get => _segments; private set { _segments = value; OnPropertyChanged(); } }

    public bool ShowMicrophone { get => _showMicrophone; set { _showMicrophone = value; OnPropertyChanged(); ApplyFilter(); } }
    public bool ShowSystemAudio { get => _showSystemAudio; set { _showSystemAudio = value; OnPropertyChanged(); ApplyFilter(); } }

    private void ApplyFilter() => Segments = _allSegments.Where(segment => segment.Source switch
    {
        "microphone" => ShowMicrophone,
        "system_audio" => ShowSystemAudio,
        _ => true
    }).ToArray();

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record TranscriptSegmentViewModel(string Timestamp, string SpeakerLabel, string SourceLabel,
    string? Source, string? SpeakerAssignment, string Text);
