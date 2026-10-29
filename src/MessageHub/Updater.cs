using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MessageHub;

// Checks GitHub for a newer release and swaps the running exe for it.
// Windows lets a running exe be renamed, so the old one moves aside to .old and is deleted next time.
static class Updater
{
    const string LatestRelease = "https://api.github.com/repos/hkanaris/message-hub/releases/latest";

    public sealed record Release(Version Version, string Tag, string DownloadUrl);

    // Static initialisers run in order: Current must be set before CreateClient uses it.
    public static Version Current { get; } = Normalise(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

    static readonly HttpClient Http = CreateClient();

    static string ExePath => Environment.ProcessPath!;

    // Gives up after 20 seconds, so a blocked or slow network can't leave a check hanging.
    public static async Task<Release?> GetLatestAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestRelease, timeout.Token));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var version)) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == "MessageHub.exe")
                return new Release(Normalise(version), tag, asset.GetProperty("browser_download_url").GetString()!);
        }

        return null;
    }

    public static async Task InstallAsync(Release release)
    {
        var newPath = ExePath + ".new";
        var oldPath = ExePath + ".old";

        using (var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(newPath);
            await response.Content.CopyToAsync(file);
        }

        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(ExePath, oldPath);
        File.Move(newPath, ExePath);
    }

    public static void CleanUp()
    {
        foreach (var leftover in new[] { ExePath + ".old", ExePath + ".new" })
        {
            try
            {
                if (File.Exists(leftover)) File.Delete(leftover);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MessageHub/{Current}");
        return client;
    }
}
