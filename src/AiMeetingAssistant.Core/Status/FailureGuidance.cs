namespace AiMeetingAssistant.Core.Status;

public enum ProblemCode { Unknown, Cancelled, DiskFull, AccessDenied, Network, ModelMissing, RuntimeMissing, DeviceUnavailable }

public sealed record FailureGuidance(ProblemCode Code, string Message)
{
    public static FailureGuidance FromException(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException) return For(ProblemCode.Cancelled);
            if (current is UnauthorizedAccessException) return For(ProblemCode.AccessDenied);
            if (current is IOException && (current.HResult & 0xffff) is 39 or 112) return For(ProblemCode.DiskFull);
            if (current is System.Net.Http.HttpRequestException or TimeoutException) return For(ProblemCode.Network);
            var classified = FromMessage(current.Message);
            if (classified.Code != ProblemCode.Unknown) return classified;
        }
        return For(ProblemCode.Unknown);
    }

    // Only fixed categories and guidance are retained. Input can contain private worker output.
    public static FailureGuidance FromMessage(string message)
    {
        var text = message.ToLowerInvariant();
        if (Has(text, "no space left", "not enough space", "disk full", "insufficient disk", "free space", "free disk")) return For(ProblemCode.DiskFull);
        if (Has(text, "permission denied", "access is denied", "access denied", "unauthorizedaccess")) return For(ProblemCode.AccessDenied);
        if (Has(text, "timed out", "timeout", "connection", "network", "resolve host", "name resolution", "certificate verify", "http error")) return For(ProblemCode.Network);
        if (text.Contains("model") && Has(text, "missing", "not found", "not installed", "incomplete", "select an installed")) return For(ProblemCode.ModelMissing);
        if (Has(text, "no module named", "dll load failed", "outdated ai runtime", "download the ai components", "ai worker unavailable", "ai components could not", "python version is not supported")) return For(ProblemCode.RuntimeMissing);
        if (Has(text, "device discovery failed", "device invalidated", "system audio error", "microphone error", "capture sources")) return For(ProblemCode.DeviceUnavailable);
        return For(ProblemCode.Unknown);
    }

    private static bool Has(string text, params string[] fragments) => fragments.Any(text.Contains);

    public static FailureGuidance For(ProblemCode code) => new(code, code switch
    {
        ProblemCode.Cancelled => "The operation was cancelled. You can retry when ready.",
        ProblemCode.DiskFull => "There may not be enough free space. Free space on the installation or recording drive, then retry.",
        ProblemCode.AccessDenied => "The location could not be accessed. Choose a writable folder or check its permissions, then retry.",
        ProblemCode.Network => "The download or connection could not complete. Check your internet connection and retry. If the service is busy, try again later.",
        ProblemCode.ModelMissing => "The speech model is missing or incomplete. Install the selected model in setup or Settings > Models, then retry.",
        ProblemCode.RuntimeMissing => "The AI components could not be used. Run welcome setup from Settings and retry the AI download. Recording-only mode remains available.",
        ProblemCode.DeviceUnavailable => "An audio or screen source could not be used. Refresh the sources and select an available Windows device, then retry.",
        _ => "The operation could not finish. Check the selected settings and retry. If it happens again, export a diagnosis report from Settings > Diagnostics."
    });
}
