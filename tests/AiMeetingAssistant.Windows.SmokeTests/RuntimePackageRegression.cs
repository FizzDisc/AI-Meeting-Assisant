using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using AiMeetingAssistant.Windows.Worker;

internal static class RuntimePackageRegression
{
    public static async Task RunAsync()
    {
        var updates = new List<RuntimeInstallProgress>();
        var parser = new PipDownloadProgress(new ImmediateProgress(updates.Add), "Libraries");
        parser.Report("Downloading first.whl (1 MB)");
        parser.Report("Progress 500 of 1000");
        if (updates[^1].CompletedBytes != 500 || updates[^1].TotalBytes != 1000 || !updates[^1].Phase.Contains("first.whl"))
            throw new Exception("pip byte progress was not parsed.");
        parser.Report("Downloading https://download.pytorch.org/whl/cpu/second%2Bcpu.whl (2 MB)");
        parser.Report("Progress 50 of 2000");
        if (updates[^1].CompletedBytes != 50 || !updates[^1].Phase.Contains("second+cpu.whl"))
            throw new Exception("pip progress did not reset for the next file.");
        parser.Report("Progress 60 of 0");
        if (updates[^1].TotalBytes != 0) throw new Exception("Unknown package total was fabricated.");
        parser.Report("Installing collected packages: first, second");
        if (updates[^1].TotalBytes != 0 || !updates[^1].Phase.Contains("Installing"))
            throw new Exception("Package installation kept a stale download percentage.");
        var root = Path.Combine(Path.GetTempPath(), "runtime-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bytes = Archive(false);
            var package = Package(bytes);
            var handler = new ResponseHandler(bytes);
            using var client = new HttpClient(handler);
            var installer = new RuntimePackageInstaller(client);
            var phases = new List<RuntimeInstallProgress>();
            var progress = new ImmediateProgress(item => phases.Add(item));
            var validations = 0;
            Task Validate(string python, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (!File.Exists(python) || !File.Exists(Path.Combine(Path.GetDirectoryName(python)!, "ffmpeg.exe")))
                    throw new Exception("Incomplete runtime reached validation.");
                validations++;
                return Task.CompletedTask;
            }
            var good = Path.Combine(root, "good");
            var target = await installer.InstallAsync(package, good, progress, Validate, default);
            if (!File.Exists(Path.Combine(target, "python.exe")) || File.ReadAllText(Path.Combine(good, "active-runtime.txt")) != package.Sha256)
                throw new Exception("Verified runtime was not activated.");
            if (!phases.Any(p => p.Phase == "Downloading AI components" && p.CompletedBytes == package.DownloadBytes)
                || !phases.Any(p => p.Phase == "Installing AI components") || !phases.Any(p => p.Phase == "Ready"))
                throw new Exception("Download/install progress is missing or inaccurate.");
            await installer.InstallAsync(package, good, progress, Validate, default);
            if (handler.Calls != 1 || validations != 2) throw new Exception("Retry redownloaded an already installed runtime.");

            var alternateId = new string('b', 64);
            var alternate = await installer.InstallAsync(package, good, progress, Validate, default, alternateId);
            if (alternate == target || Path.GetFileName(alternate) != alternateId || !File.Exists(Path.Combine(target, "python.exe")))
                throw new Exception("A new dependency identity overwrote the previous runtime.");

            async Task ExpectFailure(string name, RuntimePackage descriptor, HttpClient http,
                Func<string, CancellationToken, Task>? validate = null, CancellationToken token = default,
                IProgress<RuntimeInstallProgress>? reporter = null)
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "active-runtime.txt"), "previous-runtime");
                var failed = false;
                try { await new RuntimePackageInstaller(http).InstallAsync(descriptor, directory, reporter ?? progress, validate ?? Validate, token); }
                catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or InvalidOperationException) { failed = true; }
                if (!failed) throw new Exception(name + " was accepted.");
                if (File.ReadAllText(Path.Combine(directory, "active-runtime.txt")) != "previous-runtime")
                    throw new Exception(name + " replaced the working runtime.");
                if (Directory.Exists(Path.Combine(directory, "runtimes")) && Directory.EnumerateFileSystemEntries(Path.Combine(directory, "runtimes")).Any())
                    throw new Exception(name + " left partial files behind.");
            }
            await ExpectFailure("bad-hash", package with { Sha256 = new string('0', 64) }, client);
            using var truncated = new HttpClient(new ResponseHandler(bytes[..^4], unknownLength: true));
            await ExpectFailure("truncated", package, truncated);
            var unsafeBytes = Archive(true);
            using var unsafeClient = new HttpClient(new ResponseHandler(unsafeBytes));
            await ExpectFailure("unsafe-path", Package(unsafeBytes), unsafeClient);
            if (File.Exists(Path.Combine(root, "escape.txt"))) throw new Exception("ZIP escaped its destination.");
            await ExpectFailure("bad-runtime", package, client, (_, _) => throw new InvalidOperationException("Import check failed."));
            using var cancellation = new CancellationTokenSource();
            var cancelProgress = new ImmediateProgress(update => { if (update.Phase == "Installing AI components") cancellation.Cancel(); });
            await ExpectFailure("cancelled", package, client, token: cancellation.Token, reporter: cancelProgress);
            using var missing = new HttpClient(new ResponseHandler(bytes, status: HttpStatusCode.NotFound));
            await ExpectFailure("missing-release", package, missing);
        }
        finally { Directory.Delete(root, true); }
    }

    private static byte[] Archive(bool unsafePath)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "python.exe", "ffmpeg.exe", unsafePath ? "../../escape.txt" : "Lib/package.py" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write("test");
            }
        return stream.ToArray();
    }

    private static RuntimePackage Package(byte[] bytes) => new("test", "https://downloads.example.test/runtime.zip",
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length, 12);
    private sealed class ImmediateProgress(Action<RuntimeInstallProgress> report) : IProgress<RuntimeInstallProgress>
    {
        public void Report(RuntimeInstallProgress value) => report(value);
    }
    private sealed class ResponseHandler(byte[] bytes, bool unknownLength = false, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            HttpContent content = unknownLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
