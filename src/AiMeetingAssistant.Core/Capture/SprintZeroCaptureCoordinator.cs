namespace AiMeetingAssistant.Core.Capture;

public sealed class SprintZeroCaptureCoordinator : ICaptureCoordinator
{
    public Task<IReadOnlyList<CaptureSource>> DiscoverSourcesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CaptureSource> sources =
        [
            new("pending-screen", "Screen selection follows in Sprint 1", CaptureSourceKind.Screen, false),
            new("pending-system", "System / Teams audio follows in Sprint 1", CaptureSourceKind.SystemAudio, false),
            new("pending-microphone", "Microphone selection follows in Sprint 1", CaptureSourceKind.Microphone, false)
        ];
        return Task.FromResult(sources);
    }

    public Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Capture is intentionally not implemented in Sprint 0.");

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

