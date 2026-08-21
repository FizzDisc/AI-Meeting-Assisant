namespace AiMeetingAssistant.Core.Capture;

public enum AudioSignalHealthState { Inactive, Waiting, Healthy, CurrentlySilent, NeverDetected }

public sealed class AudioSignalHealthMonitor(TimeSpan? initialGrace = null, TimeSpan? silenceWarning = null,
    double signalThresholdDb = -55)
{
    private readonly object _gate = new();
    private readonly TimeSpan _initialGrace = initialGrace ?? TimeSpan.FromSeconds(30);
    private readonly TimeSpan _silenceWarning = silenceWarning ?? TimeSpan.FromSeconds(45);
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastSignalAt;

    public void Start(DateTimeOffset now) { lock (_gate) { _startedAt = now; _lastSignalAt = null; } }
    public void Stop() { lock (_gate) { _startedAt = null; _lastSignalAt = null; } }
    public void Observe(double rmsDb, DateTimeOffset now)
    {
        if (!double.IsFinite(rmsDb) || rmsDb < signalThresholdDb) return;
        lock (_gate) { if (_startedAt is not null) _lastSignalAt = now; }
    }
    public AudioSignalHealthState Evaluate(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_startedAt is null) return AudioSignalHealthState.Inactive;
            if (_lastSignalAt is null)
                return now - _startedAt >= _initialGrace ? AudioSignalHealthState.NeverDetected : AudioSignalHealthState.Waiting;
            return now - _lastSignalAt >= _silenceWarning ? AudioSignalHealthState.CurrentlySilent : AudioSignalHealthState.Healthy;
        }
    }
}
