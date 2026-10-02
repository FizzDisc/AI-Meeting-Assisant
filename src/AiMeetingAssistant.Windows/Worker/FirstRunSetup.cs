using System.Diagnostics;

namespace AiMeetingAssistant.Windows.Worker;

public sealed class FirstRunSetup
{
    public static bool HasModel(string? directory) => directory is not null
        && new[] { "config.json", "tokenizer.json", "model.bin" }.All(name => File.Exists(Path.Combine(directory, name)))
        && new FileInfo(Path.Combine(directory, "model.bin")).Length >= 1_000_000;

    public static void CheckWritableDirectory(string directory)
    {
        Directory.CreateDirectory(Path.GetFullPath(directory));
        var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        stream.WriteByte(0);
    }

    public static Task ValidateRuntimeAsync(string python, CancellationToken token) => RunAsync(python,
        ["-I", "-c", "import sys,struct,subprocess; from pathlib import Path; assert (3,10)<=sys.version_info[:2]<(3,14) and struct.calcsize('P')==8; assert Path(sys.prefix).resolve()==Path(sys.executable).resolve().parent; import torch,whisperx,ctranslate2,pyannote.audio,truststore; subprocess.run([str(Path(sys.executable).parent/'ffmpeg.exe'),'-version'],check=True,stdout=subprocess.DEVNULL); print('AI components ready')"],
        _ => { }, token);

    public static Task InstallModelAsync(string script, string modelId, Action<string> report, CancellationToken token)
        => RunAsync(PythonRuntimeResolver.Resolve(), ["-u", script, "install", "--model-id", modelId, "--models-root", UserAiPaths.Models], report, token);

    public static async Task RunAsync(string executable, IReadOnlyList<string> arguments, Action<string> report, CancellationToken token, bool isolatedSetup = false)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        UserAiPaths.ConfigureProcess(start);
        if (isolatedSetup)
        {
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("PIP_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            start.Environment["PIP_CONFIG_FILE"] = "NUL";
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Setup could not start the required program.");
        var tail = new Queue<string>();
        async Task Pump(StreamReader reader)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                lock (tail) { tail.Enqueue(line); while (tail.Count > 8) tail.Dequeue(); }
                report(line);
            }
        }
        var pumps = Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError));
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            await pumps.ConfigureAwait(false);
            throw;
        }
        await pumps.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string detail; lock (tail) detail = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException($"Setup failed (exit {process.ExitCode}). {detail}");
        }
    }
}
