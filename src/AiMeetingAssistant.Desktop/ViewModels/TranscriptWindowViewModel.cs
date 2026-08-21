using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiMeetingAssistant.Core.Transcripts;

namespace AiMeetingAssistant.Desktop.ViewModels;

public sealed class TranscriptWindowViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<TranscriptSegmentViewModel> _allSegments = [];
    private IReadOnlyDictionary<string, string> _speakerNames;
    private bool _showMicrophone = true;
    private bool _showSystemAudio = true;
    private IReadOnlyList<TranscriptSegmentViewModel> _segments = [];
    private IReadOnlyList<SpeakerFilterOption> _speakerOptions = [];
    private SpeakerFilterOption? _selectedSpeaker;
    private TranscriptSegmentViewModel? _selectedSegment;
    private string _searchText = string.Empty;
    private IReadOnlyList<TranscriptSegmentViewModel> _searchMatches = [];
    private IReadOnlyList<SpeakerStatisticViewModel> _speakerStatistics = [];

    public TranscriptWindowViewModel(TranscriptDocument document, string sourcePath,
        IReadOnlyDictionary<string, string>? speakerNames = null)
    {
        Document = document;
        SourcePath = sourcePath;
        _speakerNames = speakerNames ?? new Dictionary<string, string>();
        RebuildSegments();
    }

    public void UpdateSpeakerNames(IReadOnlyDictionary<string, string> speakerNames)
    {
        _speakerNames = speakerNames;
        RebuildSegments();
    }

    private void RebuildSegments()
    {
        var selectedSpeakerKey = _selectedSpeaker?.Key ?? SpeakerFilterOption.AllKey;
        _allSegments = Document.Segments.Select((segment, index) => new TranscriptSegmentViewModel(
            index, segment.Start, segment.End,
            TranscriptDocumentStore.FormatTimestamp(segment.Start),
            TranscriptDocumentStore.FormatSpeaker(segment, _speakerNames),
            TranscriptDocumentStore.FormatSource(segment.Source), segment.Source,
            segment.SpeakerAssignment, segment.Text, GetSpeakerFilterKey(segment))).ToArray();

        var options = new List<SpeakerFilterOption> { SpeakerFilterOption.All };
        options.AddRange(_allSegments
            .Where(segment => segment.SpeakerFilterKey != SpeakerFilterOption.UnknownKey)
            .GroupBy(segment => segment.SpeakerFilterKey, StringComparer.Ordinal)
            .Select(group => new SpeakerFilterOption(group.Key, group.First().SpeakerLabel))
            .OrderBy(option => option.Key == "You" ? 0 : 1)
            .ThenBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase));
        if (_allSegments.Any(segment => segment.SpeakerFilterKey == SpeakerFilterOption.UnknownKey))
            options.Add(SpeakerFilterOption.Unknown);

        SpeakerOptions = options;
        SelectedSpeaker = options.FirstOrDefault(option => option.Key == selectedSpeakerKey) ?? SpeakerFilterOption.All;
        ApplyFilter();
    }

    private static string GetSpeakerFilterKey(TranscriptSegment segment) =>
        !string.IsNullOrWhiteSpace(segment.Speaker)
        && segment.SpeakerAssignment is not ("ambiguous" or "unassigned" or "not-run")
            ? segment.Speaker
            : SpeakerFilterOption.UnknownKey;

    public event PropertyChangedEventHandler? PropertyChanged;
    public TranscriptDocument Document { get; }
    public IReadOnlyDictionary<string, string> SpeakerNames => _speakerNames;
    public string SourcePath { get; }
    public string Summary => $"{Document.Segments.Count} segments · {Document.SpeakerCount ?? 0} detected system speaker(s) · {Document.Language ?? "multiple/unknown"} · {Document.Device ?? "unknown"}/{Document.ComputeType ?? "unknown"}";
    public IReadOnlyList<TranscriptSegmentViewModel> Segments { get => _segments; private set { _segments = value; OnPropertyChanged(); } }
    public IReadOnlyList<SpeakerFilterOption> SpeakerOptions { get => _speakerOptions; private set { _speakerOptions = value; OnPropertyChanged(); } }
    public IReadOnlyList<SpeakerStatisticViewModel> SpeakerStatistics { get => _speakerStatistics; private set { _speakerStatistics = value; OnPropertyChanged(); } }
    public SpeakerFilterOption? SelectedSpeaker
    {
        get => _selectedSpeaker;
        set
        {
            if (Equals(_selectedSpeaker, value)) return;
            _selectedSpeaker = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }
    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }
    public TranscriptSegmentViewModel? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            if (ReferenceEquals(_selectedSegment, value)) return;
            _selectedSegment = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentMatchIndex));
            OnPropertyChanged(nameof(CurrentMatchSummary));
            OnPropertyChanged(nameof(CanGoToPreviousMatch));
            OnPropertyChanged(nameof(CanGoToNextMatch));
        }
    }
    public int SearchMatchCount => _searchMatches.Count;
    public int CurrentMatchIndex
    {
        get
        {
            var index = SelectedSegment is null ? -1 : IndexOfReference(_searchMatches, SelectedSegment);
            return index < 0 ? 0 : index + 1;
        }
    }
    public string CurrentMatchSummary => SearchMatchCount == 0
        ? (string.IsNullOrWhiteSpace(SearchText) ? "Enter a search term" : "No matches")
        : $"{CurrentMatchIndex} of {SearchMatchCount}";
    public bool CanGoToPreviousMatch => SearchMatchCount > 0;
    public bool CanGoToNextMatch => SearchMatchCount > 0;
    public int VisibleSegmentCount => Segments.Count;
    public int TotalSegmentCount => _allSegments.Count;
    public string FilterResultSummary => VisibleSegmentCount == TotalSegmentCount
        ? $"{TotalSegmentCount} segments"
        : $"{VisibleSegmentCount} of {TotalSegmentCount} segments";

    public bool ShowMicrophone { get => _showMicrophone; set { if (_showMicrophone == value) return; _showMicrophone = value; OnPropertyChanged(); ApplyFilter(); } }
    public bool ShowSystemAudio { get => _showSystemAudio; set { if (_showSystemAudio == value) return; _showSystemAudio = value; OnPropertyChanged(); ApplyFilter(); } }

    public void GoToPreviousMatch() => MoveToMatch(-1);
    public void GoToNextMatch() => MoveToMatch(1);

    private void MoveToMatch(int offset)
    {
        if (_searchMatches.Count == 0) return;
        var current = SelectedSegment is null ? -1 : IndexOfReference(_searchMatches, SelectedSegment);
        var next = current < 0
            ? (offset < 0 ? _searchMatches.Count - 1 : 0)
            : (current + offset + _searchMatches.Count) % _searchMatches.Count;
        SelectedSegment = _searchMatches[next];
    }

    private void ApplyFilter()
    {
        var speakerKey = SelectedSpeaker?.Key ?? SpeakerFilterOption.AllKey;
        var searchText = SearchText.Trim();
        Segments = _allSegments.Where(segment =>
        {
            var sourceVisible = segment.Source switch
            {
                "microphone" => ShowMicrophone,
                "system_audio" => ShowSystemAudio,
                _ => true
            };
            return sourceVisible
                && (speakerKey == SpeakerFilterOption.AllKey || segment.SpeakerFilterKey == speakerKey)
                && (searchText.Length == 0
                    || segment.Text.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
                    || segment.SpeakerLabel.Contains(searchText, StringComparison.CurrentCultureIgnoreCase));
        }).ToArray();
        foreach (var segment in _allSegments)
            segment.IsSearchMatchResult = searchText.Length > 0 && segment.IsSearchMatch(searchText);
        _searchMatches = searchText.Length == 0
            ? []
            : Segments.Where(segment => segment.IsSearchMatchResult).ToArray();

        // Keep the selected target stable while it remains visible. Otherwise select the
        // first match so the UI can scroll it into view without a second user action.
        if (_searchMatches.Count == 0)
            SelectedSegment = null;
        else if (SelectedSegment is null || IndexOfReference(_searchMatches, SelectedSegment) < 0)
            SelectedSegment = _searchMatches[0];

        var visibleIndices = Segments.Select(segment => segment.OriginalIndex).ToHashSet();
        SpeakerStatistics = _allSegments
            .GroupBy(segment => segment.SpeakerFilterKey, StringComparer.Ordinal)
            .Select(group => new SpeakerStatisticViewModel(
                group.Key,
                group.Key == SpeakerFilterOption.UnknownKey ? SpeakerFilterOption.Unknown.Label : group.First().SpeakerLabel,
                group.Count(segment => visibleIndices.Contains(segment.OriginalIndex)),
                group.Count(),
                group.Where(segment => visibleIndices.Contains(segment.OriginalIndex)).Sum(segment => segment.DurationSeconds),
                group.Sum(segment => segment.DurationSeconds)))
            .OrderBy(item => item.Key == "You" ? 0 : item.Key == SpeakerFilterOption.UnknownKey ? 2 : 1)
            .ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        OnPropertyChanged(nameof(VisibleSegmentCount));
        OnPropertyChanged(nameof(TotalSegmentCount));
        OnPropertyChanged(nameof(FilterResultSummary));
        OnPropertyChanged(nameof(SearchMatchCount));
        OnPropertyChanged(nameof(CurrentMatchIndex));
        OnPropertyChanged(nameof(CurrentMatchSummary));
        OnPropertyChanged(nameof(CanGoToPreviousMatch));
        OnPropertyChanged(nameof(CanGoToNextMatch));
    }

    private static int IndexOfReference(IReadOnlyList<TranscriptSegmentViewModel> items,
        TranscriptSegmentViewModel value)
    {
        for (var index = 0; index < items.Count; index++)
            if (ReferenceEquals(items[index], value)) return index;
        return -1;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record TranscriptSegmentViewModel(int OriginalIndex, double StartSeconds, double EndSeconds,
    string Timestamp, string SpeakerLabel, string SourceLabel, string? Source, string? SpeakerAssignment,
    string Text, string SpeakerFilterKey)
{
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
    public bool IsSearchMatchResult { get; internal set; }
    public bool IsSearchMatch(string searchText) => searchText.Length > 0
        && (Text.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || SpeakerLabel.Contains(searchText, StringComparison.CurrentCultureIgnoreCase));
}

public sealed record SpeakerStatisticViewModel(string Key, string Label, int VisibleSegmentCount,
    int TotalSegmentCount, double VisibleDurationSeconds, double TotalDurationSeconds)
{
    public string SegmentSummary => VisibleSegmentCount == TotalSegmentCount
        ? $"{TotalSegmentCount} segments"
        : $"{VisibleSegmentCount} of {TotalSegmentCount} segments";
    public string DurationSummary => VisibleSegmentCount == TotalSegmentCount
        ? FormatDuration(TotalDurationSeconds)
        : $"{FormatDuration(VisibleDurationSeconds)} of {FormatDuration(TotalDurationSeconds)}";

    private static string FormatDuration(double seconds) => TimeSpan.FromSeconds(seconds).TotalHours >= 1
        ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss")
        : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");
}

public sealed record SpeakerFilterOption(string Key, string Label)
{
    public string DisplayName => Label;
    public const string AllKey = "__all__";
    public const string UnknownKey = "__unknown__";
    public static SpeakerFilterOption All { get; } = new(AllKey, "All speakers");
    public static SpeakerFilterOption Unknown { get; } = new(UnknownKey, "Unknown speaker");
}
