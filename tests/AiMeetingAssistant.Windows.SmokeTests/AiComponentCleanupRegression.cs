using System.Diagnostics;
using AiMeetingAssistant.Windows.Worker;

internal static class AiComponentCleanupRegression
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var link = Path.Combine(root, "models", "outside-link");
        try
        {
            foreach (var name in new[] { "models", "runtime", "runtimes", "Recordings", "outside" })
            {
                Directory.CreateDirectory(Path.Combine(root, name));
                File.WriteAllText(Path.Combine(root, name, "keep.txt"), "test");
            }
            File.WriteAllText(Path.Combine(root, "settings.json"), "settings");
            File.WriteAllText(Path.Combine(root, "active-runtime.txt"), "pointer");
            var recordings = Path.Combine(root, "Recordings");
            var errors = AiComponentCleanup.Remove(root, false, false, recordings);
            if (errors.Count != 0 || !File.Exists(Path.Combine(root, "models", "keep.txt")))
                throw new Exception("No selection removed user data.");
            errors = AiComponentCleanup.Remove(root, true, false, recordings);
            if (errors.Count != 0 || Directory.Exists(Path.Combine(root, "models")) || !Directory.Exists(Path.Combine(root, "runtime")))
                throw new Exception("Model-only removal did not respect the selection.");
            Directory.CreateDirectory(Path.Combine(root, "models"));
            File.WriteAllText(Path.Combine(root, "models", "keep.txt"), "model");
            errors = AiComponentCleanup.Remove(root, false, true, recordings);
            if (errors.Count != 0 || Directory.Exists(Path.Combine(root, "runtime")) || Directory.Exists(Path.Combine(root, "runtimes"))
                || File.Exists(Path.Combine(root, "active-runtime.txt")) || !File.Exists(Path.Combine(root, "models", "keep.txt")))
                throw new Exception("Runtime-only cleanup removed the wrong files.");
            if (!File.Exists(Path.Combine(recordings, "keep.txt")) || !File.Exists(Path.Combine(root, "settings.json")))
                throw new Exception("Recordings or settings were removed.");
            errors = AiComponentCleanup.Remove(root, true, false, Path.Combine(root, "models", "my-recordings"));
            if (errors.Count == 0 || !File.Exists(Path.Combine(root, "models", "keep.txt")))
                throw new Exception("A custom recording directory was not protected.");

            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, Path.Combine(root, "outside") }) start.ArgumentList.Add(argument);
            using (var process = Process.Start(start)!)
            {
                process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("Could not create the junction safety fixture: " + process.StandardError.ReadToEnd());
            }
            errors = AiComponentCleanup.Remove(root, true, false, recordings);
            if (errors.Count == 0 || !File.Exists(Path.Combine(root, "outside", "keep.txt")) || !File.Exists(Path.Combine(root, "models", "keep.txt")))
                throw new Exception("Cleanup followed a directory junction or deleted before preflight completed.");
        }
        finally
        {
            // Only the generated test junction itself is removed, never its target.
            if (Directory.Exists(link)) Directory.Delete(link, false);
            Directory.Delete(root, true);
        }
    }
}
