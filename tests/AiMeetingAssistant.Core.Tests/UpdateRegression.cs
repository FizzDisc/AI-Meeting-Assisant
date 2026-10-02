using System.Net;
using System.Text;
using System.Text.Json;
using AiMeetingAssistant.Core.Updates;

internal static class UpdateRegression
{
    private static string Release(string tag = "v1.0.11", bool prerelease = false, string? url = null) => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft = false, prerelease, body = "## Changes\nA useful fix.",
        assets = new[] { new { name = "AI-Meeting-Assistant-1.0.11-win-x64.msi", state = "uploaded",
            browser_download_url = url ?? ReleaseUpdateClient.RepositoryUrl + "/releases/download/v1.0.11/AI-Meeting-Assistant-1.0.11-win-x64.msi" } }
    });
    private static ReleaseUpdate? Parse(string json) => ReleaseUpdateClient.Parse(Encoding.UTF8.GetBytes(json));
    private static void Require(bool value) { if (!value) throw new Exception("Update regression assertion failed."); }

    public static Task VersionsAndLinks()
    {
        var update = Parse(Release())!;
        Require(update.IsNewerThan(new Version(1, 0, 9, 0)));
        Require(!update.IsNewerThan(new Version(1, 0, 11, 0)));
        Require(!update.IsNewerThan(new Version(1, 0, 12, 0)));
        Require(Parse(Release("v1.0.10"))!.IsNewerThan(new Version(1, 0, 9)));
        Require(update.InstallerUrl is not null);
        Require(Parse(Release(prerelease: true)) is null);
        Require(Parse(Release().Replace("\"draft\":false", "\"draft\":true")) is null);
        foreach (var tag in new[] { "v1.0.12-beta", "../../evil", "1.2", "v1.0.11&x=1" })
        {
            try { Parse(Release(tag)); throw new Exception("Unsafe tag accepted."); }
            catch (InvalidDataException) { }
        }
        foreach (var url in new[] { "file:///C:/evil.exe", "https://github.com.evil.test/a", "https://example.com/installer.msi" })
            Require(Parse(Release(url: url))!.InstallerUrl is null);
        Require(Parse(Release().Replace("\"uploaded\"", "\"new\""))!.InstallerUrl is null);
        return Task.CompletedTask;
    }

    public static async Task HttpFailuresAndCancellation()
    {
        using var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var client = new ReleaseUpdateClient(http);
        handler.Response = () => new(HttpStatusCode.OK) { Content = new StringContent(Release()) };
        Require((await client.CheckAsync())!.Version == new Version(1, 0, 11));
        handler.Response = () => new(HttpStatusCode.NotFound);
        Require(await client.CheckAsync() is null);
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            handler.Response = () => new(status);
            try { await client.CheckAsync(); throw new Exception("HTTP error accepted."); }
            catch (HttpRequestException) { }
        }
        handler.Response = () => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1024 * 1024 + 1)) };
        try { await client.CheckAsync(); throw new Exception("Oversized response accepted."); }
        catch (InvalidDataException) { }
        handler.Response = () => new(HttpStatusCode.OK) { Content = new StringContent("not json") };
        try { await client.CheckAsync(); throw new Exception("Invalid response accepted."); }
        catch (JsonException) { }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await client.CheckAsync(cancelled.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Response { get; set; } = () => new(HttpStatusCode.NotFound);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(request.RequestUri!.Host == "api.github.com");
            Require(request.Headers.Authorization is null);
            return Task.FromResult(Response());
        }
    }
}
