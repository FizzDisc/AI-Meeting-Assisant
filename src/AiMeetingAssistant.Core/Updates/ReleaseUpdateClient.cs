using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiMeetingAssistant.Core.Updates;

public sealed record ReleaseUpdate(Version Version, string Notes, Uri ReleaseUrl, Uri? InstallerUrl)
{
    public bool IsNewerThan(Version current) => Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
}

public sealed class ReleaseUpdateClient(HttpClient client)
{
    public const string RepositoryUrl = "https://github.com/FizzDisc/AI-Meeting-Assisant";
    private const int MaximumResponseBytes = 1024 * 1024;

    public async Task<ReleaseUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/FizzDisc/AI-Meeting-Assisant/releases/latest");
        request.Headers.UserAgent.ParseAdd("AI-Meeting-Assistant-update-check");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub is limiting update checks. Please try again later.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("Update response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > MaximumResponseBytes) throw new InvalidDataException("Update response is too large.");
            buffer.Write(chunk, 0, read);
        }
        return Parse(buffer.ToArray());
    }

    public static ReleaseUpdate? Parse(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)
            || !Version.TryParse(tag.TrimStart('v'), out var version))
            throw new InvalidDataException("The release has an unsupported version number.");
        // Construct links from the fixed repository and validated tag; never launch URLs supplied by release text.
        var releaseUrl = new Uri($"{RepositoryUrl}/releases/tag/{tag}");
        var fileName = $"AI-Meeting-Assistant-{version.ToString(3)}-win-x64.msi";
        var installerUrl = new Uri($"{RepositoryUrl}/releases/download/{tag}/{fileName}");
        var hasInstaller = root.TryGetProperty("assets", out var assets) && assets.EnumerateArray().Any(asset =>
            asset.GetProperty("name").GetString() == fileName &&
            asset.GetProperty("browser_download_url").GetString() == installerUrl.AbsoluteUri &&
            asset.GetProperty("state").GetString() == "uploaded");
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        if (notes.Length > 16000) notes = notes[..16000] + "\nSee the release page for full notes.";
        return new(version, notes, releaseUrl, hasInstaller ? installerUrl : null);
    }
}
