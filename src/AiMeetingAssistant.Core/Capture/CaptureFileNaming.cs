namespace AiMeetingAssistant.Core.Capture;

public static class CaptureFileNaming
{
    public static string CreateUniqueDirectory(string parentDirectory, string directoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        Directory.CreateDirectory(parentDirectory);
        var path = Path.Combine(parentDirectory, directoryName);
        for (var counter = 1; Directory.Exists(path) && counter <= 99; counter++)
            path = Path.Combine(parentDirectory, $"{directoryName}_{counter:D2}");
        if (Directory.Exists(path)) throw new IOException($"Unable to create a unique directory for {directoryName}.");
        Directory.CreateDirectory(path);
        return path;
    }

    public static string CreateUniqueWavPath(string directory, string streamName, DateTime timestamp)
        => CreateUniquePath(directory, streamName, ".wav", timestamp);

    public static string CreateUniqueMp4Path(string directory, string streamName, DateTime timestamp)
        => CreateUniquePath(directory, streamName, ".mp4", timestamp);

    public static string CreateUniquePath(string directory, string streamName, string extension, DateTime timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        if (!extension.StartsWith('.')) extension = $".{extension}";
        Directory.CreateDirectory(directory);

        var stem = $"{streamName}_{timestamp:yyyyMMdd_HHmmss_fff}";
        var path = Path.Combine(directory, $"{stem}{extension}");
        for (var counter = 1; File.Exists(path) && counter <= 99; counter++)
            path = Path.Combine(directory, $"{stem}_{counter:D2}{extension}");

        if (File.Exists(path)) throw new IOException($"Unable to create a unique {extension} path for {streamName}.");
        return path;
    }
}
