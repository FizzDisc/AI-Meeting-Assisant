using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace AiMeetingAssistant.Windows.Worker;

public sealed record RuntimePackage(string Version, string Url, string Sha256, long DownloadBytes, long InstalledBytes)
{
    public static RuntimePackage Load(string path)
    {
        var package = JsonSerializer.Deserialize<RuntimePackage>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The AI download information is missing. Reinstall the application.");
        package.Validate();
        return package;
    }

    public void Validate()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            string.IsNullOrEmpty(Sha256) || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit) || DownloadBytes <= 0 || InstalledBytes <= 0
            || DownloadBytes > long.MaxValue - InstalledBytes - 512L * 1024 * 1024)
            throw new InvalidDataException("The AI download information is invalid. Reinstall the application.");
    }

    public long RequiredFreeBytes => checked(DownloadBytes + InstalledBytes + 512L * 1024 * 1024);
}

public sealed record RuntimeInstallProgress(string Phase, long CompletedBytes, long TotalBytes, double BytesPerSecond = 0);

/// <summary>Installs a hash-pinned, relocatable runtime without system Python or pip.</summary>
public sealed class RuntimePackageInstaller(HttpClient client)
{
    public async Task<string> InstallAsync(RuntimePackage package, string root, IProgress<RuntimeInstallProgress> progress,
        Func<string, CancellationToken, Task> validateRuntime, CancellationToken token, string? installationId = null)
    {
        package.Validate();
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        // A file lock also prevents two application instances from installing simultaneously.
        await using var installLock = new FileStream(Path.Combine(root, ".runtime-install.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var runtimes = Path.Combine(root, "runtimes");
        Directory.CreateDirectory(runtimes);
        var id = (installationId ?? package.Sha256).ToLowerInvariant();
        if (id.Length != 64 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid runtime installation identifier.");
        var destination = Path.Combine(runtimes, id);
        var pointer = Path.Combine(root, "active-runtime.txt");
        if (File.Exists(Path.Combine(destination, "python.exe")))
        {
            progress.Report(new("Checking installed components", 0, 0));
            await validateRuntime(Path.Combine(destination, "python.exe"), token).ConfigureAwait(false);
            await ActivateAsync(pointer, id, token).ConfigureAwait(false);
            return destination;
        }

        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.AvailableFreeSpace < package.RequiredFreeBytes)
            throw new IOException($"Not enough free disk space. Free at least {package.RequiredFreeBytes / 1_073_741_824d:N1} GiB and retry.");
        var staging = Path.Combine(runtimes, ".install-" + Guid.NewGuid().ToString("N"));
        var archivePath = staging + ".zip";
        var ownsDestination = false;
        var activated = false;
        Directory.CreateDirectory(staging);
        try
        {
            await DownloadAsync(package, archivePath, progress, token).ConfigureAwait(false);
            progress.Report(new("Verifying download", 0, 0));
            await using (var input = File.OpenRead(archivePath))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
                if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The download failed its integrity check. Please retry.");
            }
            await ExtractAsync(archivePath, staging, package.InstalledBytes, progress, token).ConfigureAwait(false);
            progress.Report(new("Checking AI components", 0, 0));
            if (!File.Exists(Path.Combine(staging, "python.exe")))
                throw new InvalidDataException("The AI package is incomplete.");
            // Move before executing Python: Windows/AV may retain handles to newly
            // loaded DLLs even after the validation process has exited.
            await RetryFileOperationAsync(() => Directory.Move(staging, destination), token).ConfigureAwait(false);
            ownsDestination = true;
            await validateRuntime(Path.Combine(destination, "python.exe"), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await ActivateAsync(pointer, id, token).ConfigureAwait(false);
            activated = true;
            progress.Report(new("Ready", package.InstalledBytes, package.InstalledBytes));
            return destination;
        }
        finally
        {
            // Both paths are generated under the app-owned runtimes directory above.
            if (Directory.Exists(staging)) await RetryFileOperationAsync(() => Directory.Delete(staging, true), CancellationToken.None).ConfigureAwait(false);
            if (ownsDestination && !activated && Directory.Exists(destination))
                await RetryFileOperationAsync(() => Directory.Delete(destination, true), CancellationToken.None).ConfigureAwait(false);
            if (File.Exists(archivePath)) File.Delete(archivePath);
        }
    }

    private static async Task RetryFileOperationAsync(Action action, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { action(); return; }
            catch (IOException) when (attempt < 30) { }
            catch (UnauthorizedAccessException) when (attempt < 30) { }
            await Task.Delay(500, token).ConfigureAwait(false);
        }
    }

    private async Task DownloadAsync(RuntimePackage package, string archivePath,
        IProgress<RuntimeInstallProgress> progress, CancellationToken token)
    {
        progress.Report(new("Connecting", 0, package.DownloadBytes));
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(45));
        HttpResponseMessage response;
        try { response = await client.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, connectTimeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("Could not connect to the download server. Check your connection and retry."); }
        using var responseLease = response;
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new IOException("The AI package is not available at the download server. Please try again later or update the application.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != package.DownloadBytes)
            throw new InvalidDataException("The server returned an unexpected AI package size.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        var buffer = new byte[131072];
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        long received = 0;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(TimeSpan.FromSeconds(45));
            int count;
            try { count = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new IOException("The download stopped responding. Check your connection and retry."); }
            if (count == 0) break;
            received += count;
            if (received > package.DownloadBytes) throw new InvalidDataException("The download exceeds its expected size.");
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            if (clock.Elapsed - lastReport >= TimeSpan.FromMilliseconds(150))
            {
                progress.Report(new("Downloading AI components", received, package.DownloadBytes, received / Math.Max(.001, clock.Elapsed.TotalSeconds)));
                lastReport = clock.Elapsed;
            }
        }
        if (received != package.DownloadBytes) throw new IOException("The download was incomplete. Please retry.");
        progress.Report(new("Downloading AI components", received, package.DownloadBytes, received / Math.Max(.001, clock.Elapsed.TotalSeconds)));
    }

    private static async Task ExtractAsync(string archivePath, string staging, long expectedBytes,
        IProgress<RuntimeInstallProgress> progress, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        long total = 0;
        foreach (var entry in archive.Entries) total = checked(total + entry.Length);
        if (total != expectedBytes) throw new InvalidDataException("The unpacked AI package size is invalid.");
        long completed = 0;
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        progress.Report(new("Installing AI components", 0, total));
        var buffer = new byte[131072];
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
            if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || entry.FullName.Contains(':') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("The AI package contains an unsafe file path.");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            int count;
            while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                completed += count;
                if (completed > expectedBytes) throw new InvalidDataException("The AI package exceeds its unpacked size.");
                if (clock.Elapsed - lastReport >= TimeSpan.FromMilliseconds(150))
                {
                    progress.Report(new("Installing AI components", completed, total));
                    lastReport = clock.Elapsed;
                }
            }
        }
        if (completed != expectedBytes) throw new InvalidDataException("The AI package could not be fully unpacked.");
    }

    private static async Task ActivateAsync(string pointer, string id, CancellationToken token)
    {
        var temporary = pointer + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, id, token).ConfigureAwait(false);
            File.Move(temporary, pointer, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
