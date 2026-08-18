namespace AiMeetingAssistant.Core.Status;

public sealed record OperationalStatusEntry(DateTimeOffset Timestamp, string Level, string Message)
{
    public string TimeLabel => Timestamp.ToString("HH:mm:ss");
}

public sealed class OperationalStatusLog
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private IReadOnlyList<OperationalStatusEntry> _entries = [];

    public OperationalStatusLog(int capacity = 50)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public IReadOnlyList<OperationalStatusEntry> Add(string level, string message, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(level) || string.IsNullOrWhiteSpace(message)) return Snapshot();
        lock (_lock)
        {
            if (_entries.FirstOrDefault() is { } latest && latest.Level == level && latest.Message == message)
                return _entries;
            _entries = [new(timestamp ?? DateTimeOffset.Now, level, message), .. _entries.Take(_capacity - 1)];
            return _entries;
        }
    }

    public IReadOnlyList<OperationalStatusEntry> Clear()
    {
        lock (_lock) return _entries = [];
    }

    public IReadOnlyList<OperationalStatusEntry> Snapshot()
    {
        lock (_lock) return _entries;
    }
}
