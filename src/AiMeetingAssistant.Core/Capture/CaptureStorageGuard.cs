namespace AiMeetingAssistant.Core.Capture;

public static class CaptureStorageGuard
{
    public const long DefaultRequiredBytes = 2L * 1024 * 1024 * 1024;
    public static void EnsureAvailable(string targetDirectory, long requiredBytes = DefaultRequiredBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        var root = Path.GetPathRoot(Path.GetFullPath(targetDirectory)) ?? throw new InvalidOperationException("Capture storage has no drive root.");
        var available = new DriveInfo(root).AvailableFreeSpace;
        if (!HasRequiredSpace(available, requiredBytes))
            throw new IOException($"Not enough free space for a recording. At least {FormatGiB(requiredBytes)} GiB is required; {FormatGiB(available)} GiB is available on {root}.");
    }
    public static bool HasRequiredSpace(long availableBytes, long requiredBytes) => availableBytes >= requiredBytes;
    private static string FormatGiB(long bytes) => (bytes / 1024d / 1024d / 1024d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
}
