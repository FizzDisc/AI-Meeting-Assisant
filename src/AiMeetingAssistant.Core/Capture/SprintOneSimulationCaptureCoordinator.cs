namespace AiMeetingAssistant.Core.Capture;

public sealed class SprintOneSimulationCaptureCoordinator : ICaptureCoordinator
{
    private bool _isRunning;

    public Task<IReadOnlyList<CaptureSource>> DiscoverSourcesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CaptureSource> sources =
        [
            new("pending-screen", "Screen selection follows in Sprint 1.2", CaptureSourceKind.Screen, false),
            new("pending-system", "System / Teams audio follows in Sprint 1.4", CaptureSourceKind.SystemAudio, false),
            new("pending-microphone", "Microphone selection follows in Sprint 1.3", CaptureSourceKind.Microphone, false)
        ];
        return Task.FromResult(sources);
    }

    public async Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("The simulated capture session is already running.");
        }

        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        _isRunning = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            throw new InvalidOperationException("The simulated capture session is not running.");
        }

        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        _isRunning = false;
    }
}

