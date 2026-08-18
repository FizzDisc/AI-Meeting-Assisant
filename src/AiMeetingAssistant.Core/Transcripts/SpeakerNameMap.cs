using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiMeetingAssistant.Core.Transcripts;

public sealed record SpeakerNameDocument(int SchemaVersion, IReadOnlyDictionary<string, string> Names);

public static partial class SpeakerNameStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static string PathForTranscript(string transcriptPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(transcriptPath))!, "speaker-names.json");

    public static IReadOnlyDictionary<string, string> LoadForTranscript(string transcriptPath)
    {
        var path = PathForTranscript(transcriptPath);
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
        var document = JsonSerializer.Deserialize<SpeakerNameDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Speaker-name mapping is empty.");
        if (document.SchemaVersion != 1 || document.Names is null)
            throw new InvalidDataException("Speaker-name mapping has an unsupported schema.");
        return Validate(document.Names);
    }

    public static void SaveForTranscript(string transcriptPath, IReadOnlyDictionary<string, string> names)
    {
        var validated = Validate(names);
        var path = PathForTranscript(transcriptPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new SpeakerNameDocument(1, validated), Options), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static IReadOnlyDictionary<string, string> Validate(IReadOnlyDictionary<string, string> names)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in names)
        {
            if (pair.Key != "You" && !AnonymousSpeakerPattern().IsMatch(pair.Key))
                throw new InvalidDataException($"Unsupported speaker ID: {pair.Key}");
            var value = pair.Value.Trim();
            if (value.Length == 0) continue;
            if (value.Length > 100 || value.Any(char.IsControl))
                throw new InvalidDataException($"Invalid display name for {pair.Key}.");
            result[pair.Key] = value;
        }
        return result;
    }

    [GeneratedRegex("^SPEAKER_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AnonymousSpeakerPattern();
}
