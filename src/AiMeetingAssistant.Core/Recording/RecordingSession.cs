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
                await captureCoordinator.StartAsync(plan, cancellationToken);
                TransitionTo(RecordingSessionState.Recording);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                TransitionTo(RecordingSessionState.Failed, LastError);
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
}
