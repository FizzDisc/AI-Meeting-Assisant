namespace AiMeetingAssistant.Core.Capture;

public static class CaptureFileNaming
{
    public static string CreateUniqueWavPath(string directory, string streamName, DateTime timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        Directory.CreateDirectory(directory);

        var stem = $"{streamName}_{timestamp:yyyyMMdd_HHmmss_fff}";
        var path = Path.Combine(directory, $"{stem}.wav");
        for (var counter = 1; File.Exists(path) && counter <= 99; counter++)
            path = Path.Combine(directory, $"{stem}_{counter:D2}.wav");

        if (File.Exists(path)) throw new IOException($"Unable to create a unique WAV path for {streamName}.");
        return path;
    }
}
