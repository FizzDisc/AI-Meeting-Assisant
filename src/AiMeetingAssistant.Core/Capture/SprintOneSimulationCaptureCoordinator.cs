namespace AiMeetingAssistant.Core.Capture;

public sealed class SprintOneSimulationCaptureCoordinator : ICaptureCoordinator
{
    private bool _isRunning;

#pragma warning disable CS0067
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;
#pragma warning restore CS0067

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
        if (!_isRunning) return;

        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        _isRunning = false;
    }
}
