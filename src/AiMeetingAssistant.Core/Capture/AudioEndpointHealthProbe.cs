namespace AiMeetingAssistant.Core.Capture;

public sealed record AudioEndpointSnapshot(string SourceId, string DisplayName, CaptureSourceKind Kind,
    double? PeakAmplitude, bool? IsGloballyMuted);

public interface IAudioEndpointHealthProbe
{
    Task<IReadOnlyList<AudioEndpointSnapshot>> ProbeAsync(IReadOnlyList<CaptureSource> sources,
        CancellationToken cancellationToken = default);
}

public static class AudioEndpointHealthAdvisor
{
    private const double ActiveAlternativePeak = 0.0031622776601683794; // -50 dBFS

    public static string BuildGuidance(CaptureSource selected, AudioSignalHealthState selectedState,
        IReadOnlyList<AudioEndpointSnapshot> snapshots)
    {
        if (selectedState is not (AudioSignalHealthState.NeverDetected or AudioSignalHealthState.CurrentlySilent))
            return "";
        var current = snapshots.FirstOrDefault(item => item.SourceId == selected.Id);
        if (current?.IsGloballyMuted == true)
            return $"{DisplayKind(selected.Kind)} is muted in Windows.";
        var alternative = snapshots.Where(item => item.Kind == selected.Kind && item.SourceId != selected.Id
                && item.IsGloballyMuted != true && item.PeakAmplitude >= ActiveAlternativePeak)
            .OrderByDescending(item => item.PeakAmplitude).FirstOrDefault();
        return alternative is null ? "" :
            $"Audio is active on alternative endpoint “{alternative.DisplayName}”; consider selecting it.";
    }

    private static string DisplayKind(CaptureSourceKind kind) => kind == CaptureSourceKind.Microphone
        ? "The selected microphone endpoint"
        : "The selected system-audio endpoint";
}
