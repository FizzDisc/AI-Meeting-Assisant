using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Core.Recording;

public sealed class RecordingSession(ICaptureCoordinator captureCoordinator)
{
    private readonly SemaphoreSlim _transitionLock = new(1, 1);

    public RecordingSessionState State { get; private set; } = RecordingSessionState.Idle;

    public string? LastError { get; private set; }

    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    public async Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            EnsureState(RecordingSessionState.Idle, RecordingSessionState.Completed, RecordingSessionState.Failed);
            LastError = null;
            TransitionTo(RecordingSessionState.Preparing);

            try
            {
                // Register for async capture errors during recording
                captureCoordinator.CaptureFailed += OnCaptureFailed;

                await captureCoordinator.StartAsync(plan, cancellationToken);
                TransitionTo(RecordingSessionState.Recording);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                TransitionTo(RecordingSessionState.Failed, LastError);
                captureCoordinator.CaptureFailed -= OnCaptureFailed;
                throw;
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            EnsureState(RecordingSessionState.Recording);
            TransitionTo(RecordingSessionState.Stopping);

            try
            {
                await captureCoordinator.StopAsync(cancellationToken);
                TransitionTo(RecordingSessionState.Completed);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                TransitionTo(RecordingSessionState.Failed, LastError);
                throw;
            }
            finally
            {
                // Unregister from async capture errors
                captureCoordinator.CaptureFailed -= OnCaptureFailed;
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            captureCoordinator.CaptureFailed -= OnCaptureFailed;
            await captureCoordinator.StopAsync(cancellationToken);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private void EnsureState(params RecordingSessionState[] allowedStates)
    {
        if (!allowedStates.Contains(State))
        {
            throw new InvalidOperationException($"Cannot change a recording session while it is {State}.");
        }
    }

    private void TransitionTo(RecordingSessionState newState, string? errorMessage = null)
    {
        var previousState = State;
        State = newState;
        StateChanged?.Invoke(this, new(previousState, newState, errorMessage));
    }

    private void OnCaptureFailed(object? sender, CaptureErrorEventArgs e)
    {
        _ = Task.Run(() => HandleCaptureFailedAsync(e));
    }

    private async Task HandleCaptureFailedAsync(CaptureErrorEventArgs e)
    {
        await _transitionLock.WaitAsync();
        try
        {
            if (State is not (RecordingSessionState.Recording or RecordingSessionState.Stopping)) return;

            LastError = e.ErrorMessage;
            captureCoordinator.CaptureFailed -= OnCaptureFailed;
            try
            {
                await captureCoordinator.StopAsync();
            }
            catch (Exception cleanupError)
            {
                LastError = $"{e.ErrorMessage} Cleanup failed: {cleanupError.Message}";
            }
            TransitionTo(RecordingSessionState.Failed, LastError);
        }
        finally
        {
            _transitionLock.Release();
        }
    }
}
