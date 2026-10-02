using System.Diagnostics;
using AiMeetingAssistant.Windows.Worker;

internal static class FirstRunSetupRegression
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"setup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            FirstRunSetup.CheckWritableDirectory(root);
            if (Directory.EnumerateFiles(root).Any()) throw new InvalidOperationException("Write probe left a file behind.");
            if (FirstRunSetup.HasModel(root)) throw new InvalidOperationException("Empty model was accepted.");
            File.WriteAllText(Path.Combine(root, "config.json"), "{}");
            File.WriteAllText(Path.Combine(root, "tokenizer.json"), "{}");
            File.WriteAllBytes(Path.Combine(root, "model.bin"), new byte[1_000_000]);
            if (!FirstRunSetup.HasModel(root)) throw new InvalidOperationException("Complete model was rejected.");
            var start = new ProcessStartInfo("python");
            start.Environment["PYANNOTE_METRICS_ENABLED"] = "1";
            UserAiPaths.ConfigureProcess(start);
            if (start.Environment["PYANNOTE_METRICS_ENABLED"] != "0") throw new Exception("Pyannote telemetry remained enabled.");
            if (start.Environment["PATH"]?.StartsWith(UserAiPaths.Tools + Path.PathSeparator) != true)
                throw new InvalidOperationException("User FFmpeg directory was not exposed to child processes.");
            File.WriteAllText(Path.Combine(root, "ffmpeg.exe"), "test fixture");
            var portable = new ProcessStartInfo(Path.Combine(root, "python.exe"));
            UserAiPaths.ConfigureProcess(portable);
            if (portable.Environment["PATH"]?.StartsWith(root + Path.PathSeparator) != true)
                throw new InvalidOperationException("Explicit portable runtime did not use its own FFmpeg directory.");
            var pidFile = Path.Combine(root, "pid.txt");
            using var cancellation = new CancellationTokenSource();
            var task = FirstRunSetup.RunAsync("python", ["-c",
                "import os,sys,time; from pathlib import Path; Path(sys.argv[1]).write_text(str(os.getpid())); print('ready',flush=True); time.sleep(30)", pidFile],
                line => { if (line == "ready") cancellation.Cancel(); }, cancellation.Token);
            try { await task.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Setup ignored cancellation."); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            var pid = int.Parse(File.ReadAllText(pidFile));
            try
            {
                using var process = Process.GetProcessById(pid);
                if (!process.HasExited) throw new InvalidOperationException("Cancelled setup left a process running.");
            }
            catch (ArgumentException) { }
        }
        finally { Directory.Delete(root, true); }
    }
}
