using System.Text.Json;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Transcripts;
using AiMeetingAssistant.Windows.Worker;

internal static class IncrementalQueueRegression
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"session_queue_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            // Exercise the real coordinator and JSON-lines client without ML dependencies.
            var script = Path.Combine(root, "worker.py");
            await File.WriteAllTextAsync(script, """
                import json, pathlib, sys
                for line in sys.stdin:
                    request = json.loads(line)
                    payload = request['payload']
                    if request['type'] == 'transcription.start':
                        output = pathlib.Path(payload['outputPath'])
                        output.write_text(json.dumps({'schemaVersion': 1,
                            'segments': [{'start': 0, 'end': 1, 'text': output.stem}]}))
                    elif request['type'] == 'incremental.finalize':
                        pathlib.Path(payload['outputPath']).write_text(
                            pathlib.Path(payload['mergedTranscriptPath']).read_text())
                    result = {'jobId': 'test', 'status': 'completed', 'progress': 1.0}
                    print(json.dumps({'protocolVersion': '1.0', 'requestId': request['requestId'],
                        'type': 'transcription.status', 'ok': True, 'payload': result, 'error': None}), flush=True)
                """);
            var chunks = Path.Combine(root, "processing", "live-chunks", "system_audio");
            Directory.CreateDirectory(chunks);
            await File.WriteAllTextAsync(Path.Combine(chunks, "chunks.json"), JsonSerializer.Serialize(new
            {
                Chunks = Enumerable.Range(0, 6).Select(index => new
                {
                    Index = index, FileName = $"chunk_{index}.wav", StartSeconds = index * 10,
                    DurationSeconds = 10
                })
            }));
            await File.WriteAllBytesAsync(Path.Combine(root, "system_audio_test.wav"), []);
            await using var worker = new PythonWorkerClient("python", script);
            await using var coordinator = new IncrementalTranscriptionCoordinator(worker, queueCapacity: 1);
            var options = new IncrementalTranscriptionOptions(root, "tiny", "cpu-only",
                null, null, null, null, null);
            var warnings = 0;
            coordinator.StatusChanged += (_, status) =>
            {
                if (status.Level == "WARNING") Interlocked.Increment(ref warnings);
            };
            var rejected = 0;
            for (var index = 0; index < 6; index++)
            {
                if (!coordinator.TryQueue(new(root, "system_audio", index,
                    Path.Combine(chunks, $"chunk_{index}.wav"), index * 10, 10), options)) rejected++;
            }
            if (rejected == 0 || warnings != rejected)
                throw new InvalidOperationException("Overflow must return false and report each unqueued chunk.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var output = await coordinator.FinalizeSessionAsync(root, options, timeout.Token);
            var transcript = TranscriptDocumentStore.Load(output);
            if (!transcript.Segments.Select(segment => segment.Start).SequenceEqual(
                    Enumerable.Range(0, 6).Select(index => (double)index * 10)))
                throw new InvalidOperationException("Finalization lost or duplicated overflow chunks.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
