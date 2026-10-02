using System.Text.Json;
using AiMeetingAssistant.Core.Status;

internal static class DiagnosisRegression
{
    private static void Require(bool condition) { if (!condition) throw new Exception("Diagnosis regression assertion failed."); }

    public static Task Guidance()
    {
        Require(FailureGuidance.FromException(new IOException("error", unchecked((int)0x80070070))).Code == ProblemCode.DiskFull);
        Require(FailureGuidance.FromException(new UnauthorizedAccessException()).Code == ProblemCode.AccessDenied);
        Require(FailureGuidance.FromException(new Exception("wrapper", new HttpRequestException("offline"))).Code == ProblemCode.Network);
        Require(FailureGuidance.FromException(new OperationCanceledException()).Code == ProblemCode.Cancelled);
        Require(FailureGuidance.FromMessage("AI setup needs at least 6 GiB of free disk space.").Code == ProblemCode.DiskFull);
        Require(FailureGuidance.FromMessage("The downloaded model is incomplete.").Code == ProblemCode.ModelMissing);
        Require(FailureGuidance.FromMessage("No module named faster_whisper").Code == ProblemCode.RuntimeMissing);
        Require(FailureGuidance.FromMessage("System audio error: device invalidated").Code == ProblemCode.DeviceUnavailable);
        Require(FailureGuidance.FromMessage("Unexpected condition").Code == ProblemCode.Unknown);
        return Task.CompletedTask;
    }

    public static Task ExportExcludesPrivateContent()
    {
        const string privateData = "C:\\Users\\PrivatePerson\\Meetings\\SecretMeeting.wav hf_secret123 user@example.test https://example.test/?token=secret SECRET TRANSCRIPT";
        var history = new DiagnosisHistory();
        var guidance = FailureGuidance.FromException(new IOException("No space left " + privateData));
        Require(!guidance.Message.Contains("PrivatePerson"));
        history.Add(DiagnosisArea.Setup, guidance.Code);
        var report = new DiagnosisReport(new Version(1, 0, 12), new Version(10, 0, 1), true,
            true, false, true, RuntimeCheckState.NotChecked, true, false, false, true, false, history.Snapshot());
        var json = report.ToJson();
        foreach (var secret in new[] { "PrivatePerson", "SecretMeeting", "hf_secret", "user@example", "token=secret", "SECRET TRANSCRIPT", "C:\\" })
            Require(!json.Contains(secret, StringComparison.Ordinal));
        using var parsed = JsonDocument.Parse(json);
        Require(parsed.RootElement.GetProperty("RecentEvents")[0].GetProperty("Code").GetString() == "DiskFull");
        Require(parsed.RootElement.GetProperty("LastRuntimeCheck").GetString() == "NotChecked");
        for (var i = 0; i < 100; i++) history.Add(DiagnosisArea.ModelSetup, ProblemCode.Network);
        Require(history.Snapshot().Length == 50);
        Require(history.Snapshot().All(item => item.Code == ProblemCode.Network));
        return Task.CompletedTask;
    }
}
