namespace AiMeetingAssistant.Core.Capture;

public sealed class SprintOneSimulationCaptureCoordinator : ICaptureCoordinator
{
    private bool _isRunning;

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
