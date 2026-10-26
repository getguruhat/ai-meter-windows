using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace AIMeter;

/// Asks GitHub for the app's latest release. The Microsoft Store version doesn't use
/// this: the Store keeps it up to date.
internal static class UpdateCheck
{
    public sealed record Release(Version Version, string Page);

    /// This build's version, as major.minor.patch.
    public static Version Current { get; } = Normalize(typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0));

    // After Current: static fields initialize in order, and the client's User-Agent uses it.
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // GitHub's API turns away requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GuruHat-AIMeter", Current.ToString()));
        return client;
    }

    /// The latest release when it's newer than this build; null when it isn't or GitHub can't be reached.
    public static async Task<Release?> NewerAsync(string repo)
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var version = Parse((string?)json?["tag_name"]);
            var page = (string?)json?["html_url"];
            return version is not null && page is not null && version > Current ? new Release(version, page) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    /// "v1.0.4" and "v1.5.2-windows" both give 1.0.4 / 1.5.2.
    public static Version? Parse(string? tag)
    {
        if (tag is null) return null;
        var digits = new string(tag.TrimStart('v', 'V').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        if (!digits.Contains(".")) digits += ".0";
        return Version.TryParse(digits, out var version) ? Normalize(version) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
