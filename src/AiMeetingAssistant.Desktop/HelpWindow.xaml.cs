using System.Windows;
using System.Windows.Controls;

namespace AiMeetingAssistant.Desktop;

public partial class HelpWindow : Window
{
    private static readonly IReadOnlyDictionary<string, HelpTopic> Topics =
        new Dictionary<string, HelpTopic>(StringComparer.Ordinal)
        {
            ["Capture"] = new(
                "CAPTURE",
                "Record a meeting",
                "Choose what should be captured and keep an eye on both audio meters before and during the meeting.",
                [
                    "Select the display, system audio output and microphone you want to use.",
                    "Use the Screen Capture and Live Transcription switches to reduce storage or system load when needed.",
                    "Adjust source levels below the meters, then press the large record button to start and stop.",
                    "If Teams mute detection is active, the microphone track follows the Teams mute state."
                ],
                "Speak briefly before the meeting starts. A moving microphone meter confirms that the selected device is producing audio."),
            ["Recordings"] = new(
                "RECORDINGS",
                "Find and process meetings",
                "The library collects recorded sessions and all transcript runs that belong to them.",
                [
                    "Select a meeting to inspect its date, duration, streams and transcript runs.",
                    "Start or cancel post-meeting transcription from the processing area.",
                    "Choose a run before opening its transcript when several model runs exist.",
                    "Delete only meetings you no longer need; deletion removes their local session data."
                ],
                "Give meetings a meaningful name when recording starts so they remain easy to find later."),
            ["Transcript"] = new(
                "TRANSCRIPT",
                "Review what was said",
                "The Transcript tab opens the newest available transcript by default, or the exact run selected in Recordings.",
                [
                    "Filter microphone and system-audio segments to focus on one source.",
                    "Review timestamps and detected speaker labels alongside every segment.",
                    "Assign real names to detected speakers and export the result as Markdown or JSON.",
                    "Automatic speech recognition can make mistakes; verify names, decisions and numbers before sharing."
                ],
                "For a specific older result, select the meeting and run in Recordings and choose View transcript."),
            ["Storage"] = new(
                "STORAGE",
                "Understand disk usage",
                "Storage shows how much space capture masters, transcripts and reproducible processing data consume.",
                [
                    "Refresh the inventory after large recordings or cleanup operations.",
                    "Use the session table to identify meetings that occupy the most space.",
                    "Preview cleanup before confirming it; the preview states the expected reclaimed space.",
                    "Safe cleanup protects capture masters, final transcripts, speaker names and session manifests."
                ],
                "Cleanup removes reproducible intermediate data. Keep capture masters until you are certain no new transcript run is needed."),
            ["Settings"] = new(
                "SETTINGS",
                "Configure the local workspace",
                "Settings groups recording defaults, local AI models and diagnostic information.",
                [
                    "General controls capture defaults and the session library location.",
                    "Models installs speech and diarization packages and selects CPU, NVIDIA or supported Intel acceleration.",
                    "Diagnostics shows the local settings location and points to technical activity.",
                    "Model downloads are optional and remain local; larger models improve quality but need more disk space and processing time."
                ],
                "Changing the session library location requires an application restart and does not move existing meetings automatically.")
        };

    public HelpWindow()
    {
        InitializeComponent();
        TopicList.SelectedIndex = 0;
    }

    private void OnTopicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is ListBoxItem { Tag: string topicId })
            ShowTopic(topicId);
    }

    private void ShowTopic(string topicId)
    {
        if (!Topics.TryGetValue(topicId, out var topic))
            return;

        TopicEyebrow.Text = topic.Eyebrow;
        TopicTitle.Text = topic.Title;
        TopicSummary.Text = topic.Summary;
        StepList.ItemsSource = topic.Steps;
        TopicTip.Text = topic.Tip;
    }

    private sealed record HelpTopic(
        string Eyebrow,
        string Title,
        string Summary,
        IReadOnlyList<string> Steps,
        string Tip);
}
