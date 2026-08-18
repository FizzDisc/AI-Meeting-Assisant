using System.Collections.ObjectModel;
using System.Windows;

namespace AiMeetingAssistant.Desktop;

public partial class SpeakerNamesWindow : Window
{
    public SpeakerNamesWindow(IEnumerable<string> speakerIds, IReadOnlyDictionary<string, string> existingNames)
    {
        InitializeComponent();
        Entries = new(speakerIds.Select(id => new SpeakerNameEntry(id,
            existingNames.TryGetValue(id, out var name) ? name : "")));
        DataContext = this;
    }

    public ObservableCollection<SpeakerNameEntry> Entries { get; }
    public IReadOnlyDictionary<string, string> SpeakerNames { get; private set; } = new Dictionary<string, string>();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SpeakerNames = Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.DisplayName))
            .ToDictionary(entry => entry.SpeakerId, entry => entry.DisplayName.Trim(), StringComparer.Ordinal);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class SpeakerNameEntry(string speakerId, string displayName)
{
    public string SpeakerId { get; } = speakerId;
    public string DisplayName { get; set; } = displayName;
}
