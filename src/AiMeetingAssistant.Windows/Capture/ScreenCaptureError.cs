namespace AiMeetingAssistant.Windows.Capture;

public static class ScreenCaptureError
{
    public static string Describe(string? error)
    {
        var detail = string.IsNullOrWhiteSpace(error) ? "Unknown screen capture error." : error.Trim();
        if (detail.Contains("video sink writer", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("media type", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("Medientyp", StringComparison.OrdinalIgnoreCase))
        {
            return $"The H.264 video encoder rejected this display format. Refresh displays and retry; if it persists, update the graphics driver or use another display. Details: {detail}";
        }

        if (detail.Contains("access", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("Zugriff", StringComparison.OrdinalIgnoreCase))
        {
            return $"Windows denied screen capture access. Check privacy permissions and retry. Details: {detail}";
        }

        return $"Screen capture failed. Refresh displays and retry. Details: {detail}";
    }
}
