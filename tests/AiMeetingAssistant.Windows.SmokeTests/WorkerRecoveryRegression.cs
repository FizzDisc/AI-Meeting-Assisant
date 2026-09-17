using AiMeetingAssistant.Windows.Worker;

internal static class WorkerRecoveryRegression
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"worker_recovery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var script = Path.Combine(root, "worker.py");
            await File.WriteAllTextAsync(script, """
                import json, sys, time
                for line in sys.stdin:
                    request = json.loads(line)
                    if request['type'] == 'slow':
                        time.sleep(30)
                    print(json.dumps({'protocolVersion': '1.0', 'requestId': request['requestId'],
                        'type': 'result', 'ok': True, 'payload': {}, 'error': None}), flush=True)
                """);
            await using var worker = new PythonWorkerClient("python", script);
            await worker.SendAsync("ready", new { });
            try
            {
                await worker.SendAsync("slow", new { }, responseTimeout: TimeSpan.FromMilliseconds(150));
                throw new InvalidOperationException("Expected a worker timeout.");
            }
            catch (TimeoutException) { }
            await worker.SendAsync("ready", new { }, responseTimeout: TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            try
            {
                await worker.SendAsync("slow", new { }, cancellation.Token);
                throw new InvalidOperationException("Expected request cancellation.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            await worker.SendAsync("ready", new { }, responseTimeout: TimeSpan.FromSeconds(5));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
