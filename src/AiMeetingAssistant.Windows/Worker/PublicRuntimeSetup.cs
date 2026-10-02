using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AiMeetingAssistant.Windows.Worker;

public static class PublicRuntimeSetup
{
    public const long RequiredFreeBytes = 6L * 1024 * 1024 * 1024;
    // Full portable CPython distribution, including pip, published by python.org.
    public static RuntimePackage PythonPackage { get; } = new("3.12.10",
        "https://www.python.org/ftp/python/3.12.10/python-3.12.10-amd64.zip",
        "8649692de846c56a7189d6dae5c322ab20deb1b5908b6f39426b62a36f39415d", 32399361, 120770083);

    public static async Task<string> InstallAsync(HttpClient client, string root, string requirements,
        IProgress<RuntimeInstallProgress> progress, CancellationToken token)
    {
        if (!File.Exists(requirements)) throw new FileNotFoundException("AI setup files are missing. Reinstall the application.");
        var identity = PythonPackage.Sha256 + "|pip25.3|torch2.8.0+cpu|torchaudio2.8.0+cpu|torchvision0.23.0+cpu|"
            + await File.ReadAllTextAsync(requirements, token).ConfigureAwait(false);
        var installationId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        Directory.CreateDirectory(root);
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace < RequiredFreeBytes)
            throw new IOException("AI setup needs at least 6 GiB of free disk space. Free some space and retry.");
        var bootstrapProgress = new DirectProgress(update => progress.Report(update with
        {
            Phase = update.Phase switch
            {
                "Downloading AI components" => "Step 1/4 · Downloading Python",
                "Installing AI components" => "Step 1/4 · Unpacking Python",
                "Checking AI components" => "Step 2/4 · Preparing AI libraries",
                "Ready" => "AI components ready",
                _ => update.Phase
            },
            CompletedBytes = update.Phase == "Ready" ? 0 : update.CompletedBytes,
            TotalBytes = update.Phase == "Ready" ? 0 : update.TotalBytes
        }));
        return await new RuntimePackageInstaller(client).InstallAsync(PythonPackage, root, bootstrapProgress,
            async (python, cancellation) =>
            {
                // An already completed runtime can be reused without another download.
                var directory = Path.GetDirectoryName(python)!;
                var marker = Path.Combine(directory, "public-setup-complete.txt");
                if (File.Exists(marker))
                {
                    await FirstRunSetup.ValidateRuntimeAsync(python, cancellation).ConfigureAwait(false);
                    return;
                }
                progress.Report(new("Step 2/4 · Preparing download tools", 0, 0));
                await Pip(python, ["--progress-bar", "off", "--index-url", "https://pypi.org/simple", "pip==25.3"],
                    _ => { }, cancellation).ConfigureAwait(false);
                var reporter = new PipDownloadProgress(progress, "Step 2/4 · AI engine");
                progress.Report(new("Step 2/4 · Resolving AI engine downloads", 0, 0));
                await Pip(python, ["--progress-bar", "raw", "--index-url", "https://download.pytorch.org/whl/cpu",
                    "--no-deps", "torch==2.8.0+cpu", "torchaudio==2.8.0+cpu", "torchvision==0.23.0+cpu"],
                    reporter.Report, cancellation).ConfigureAwait(false);
                reporter = new PipDownloadProgress(progress, "Step 3/4 · AI libraries");
                progress.Report(new("Step 3/4 · Resolving AI library downloads", 0, 0));
                await Pip(python, ["--progress-bar", "raw", "--index-url", "https://pypi.org/simple", "-r", Path.GetFullPath(requirements)],
                    reporter.Report, cancellation).ConfigureAwait(false);
                progress.Report(new("Step 4/4 · Checking AI components", 0, 0));
                await FirstRunSetup.RunAsync(python, ["-c", "import imageio_ffmpeg,shutil,sys; from pathlib import Path; shutil.copyfile(imageio_ffmpeg.get_ffmpeg_exe(),Path(sys.executable).parent/'ffmpeg.exe')"],
                    _ => { }, cancellation).ConfigureAwait(false);
                await FirstRunSetup.ValidateRuntimeAsync(python, cancellation).ConfigureAwait(false);
                await File.WriteAllTextAsync(marker, "Python 3.12.10 / WhisperX 3.8.6 / CPU", cancellation).ConfigureAwait(false);
            }, token, installationId).ConfigureAwait(false);
    }

    private static Task Pip(string python, string[] arguments, Action<string> report, CancellationToken token)
        => FirstRunSetup.RunAsync(python, ["-u", "-m", "pip", "--isolated", "install", "--disable-pip-version-check",
            "--no-input", "--no-cache-dir", "--timeout", "45", "--retries", "2", .. arguments], report, token, isolatedSetup: true);

    private sealed class DirectProgress(Action<RuntimeInstallProgress> report) : IProgress<RuntimeInstallProgress>
    {
        public void Report(RuntimeInstallProgress value) => report(value);
    }
}

public sealed class PipDownloadProgress(IProgress<RuntimeInstallProgress> progress, string stage)
{
    private readonly Stopwatch _clock = new();
    private string _file = "current package";
    private long _lastBytes;

    public void Report(string line)
    {
        lock (_clock)
        {
            var text = line.Trim();
            if (text.StartsWith("Downloading ", StringComparison.Ordinal))
            {
                var name = text[12..].Split(' ', 2)[0];
                _file = Uri.TryCreate(name, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                    ? Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath)) : name;
                _clock.Restart(); _lastBytes = 0;
                progress.Report(new($"{stage} · Downloading {_file}", 0, 0));
            }
            else if (Regex.Match(text, @"^Progress (\d+) of (\d+)$") is { Success: true } match
                && long.TryParse(match.Groups[1].Value, out var current) && long.TryParse(match.Groups[2].Value, out var total))
            {
                if (!_clock.IsRunning || current < _lastBytes) _clock.Restart();
                _lastBytes = current;
                progress.Report(new($"{stage} · Current file: {_file}", current, total, current / Math.Max(.001, _clock.Elapsed.TotalSeconds)));
            }
            else if (text.StartsWith("Installing collected packages", StringComparison.Ordinal)
                || text.StartsWith("Building wheels", StringComparison.Ordinal)
                || text.StartsWith("Preparing metadata", StringComparison.Ordinal))
                progress.Report(new($"{stage} · Installing / preparing packages", 0, 0));
        }
    }
}
