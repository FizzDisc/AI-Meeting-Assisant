using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;

var tests = new (string Name, Func<Task> Run)[]
{
    ("happy path follows all transitions", HappyPathFollowsAllTransitions),
    ("stop from idle is rejected", StopFromIdleIsRejected),
    ("capture start failure moves session to failed", StartFailureMovesSessionToFailed),
    ("completed session can start again", CompletedSessionCanStartAgain)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static async Task HappyPathFollowsAllTransitions()
{
    var states = new List<RecordingSessionState>();
    var session = new RecordingSession(new FakeCaptureCoordinator());
    session.StateChanged += (_, args) => states.Add(args.CurrentState);

    await session.StartAsync(TestPlan());
    await session.StopAsync();

    Equal(RecordingSessionState.Completed, session.State);
    SequenceEqual(
        [RecordingSessionState.Preparing, RecordingSessionState.Recording, RecordingSessionState.Stopping, RecordingSessionState.Completed],
        states);
}

static async Task StopFromIdleIsRejected()
{
    var session = new RecordingSession(new FakeCaptureCoordinator());
    await ThrowsAsync<InvalidOperationException>(() => session.StopAsync());
    Equal(RecordingSessionState.Idle, session.State);
}

static async Task StartFailureMovesSessionToFailed()
{
    var session = new RecordingSession(new FakeCaptureCoordinator(failOnStart: true));
    await ThrowsAsync<IOException>(() => session.StartAsync(TestPlan()));
    Equal(RecordingSessionState.Failed, session.State);
    Equal("Simulated start failure.", session.LastError);
}

static async Task CompletedSessionCanStartAgain()
{
    var session = new RecordingSession(new FakeCaptureCoordinator());
    await session.StartAsync(TestPlan());
    await session.StopAsync();
    await session.StartAsync(TestPlan());
    Equal(RecordingSessionState.Recording, session.State);
}

static CapturePlan TestPlan() => new("screen", "system", "microphone");

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException("Sequences differ.");
    }
}

static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

file sealed class FakeCaptureCoordinator(bool failOnStart = false) : ICaptureCoordinator
{
    private bool _isRunning;

    public Task<IReadOnlyList<CaptureSource>> DiscoverSourcesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CaptureSource>>([]);

    public Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        if (failOnStart)
        {
            throw new IOException("Simulated start failure.");
        }

        _isRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            throw new InvalidOperationException("Not running.");
        }

        _isRunning = false;
        return Task.CompletedTask;
    }
}

