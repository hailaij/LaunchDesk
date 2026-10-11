using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaunchDesk.Core;

public sealed record ReleaseUpdate(Version Version, string Tag, string Notes, Uri Page)
{
    public ReleaseAsset? Installer { get; init; }
    public bool IsNewerThan(Version current) => Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
}

public static class UpdateChecker
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1024 * 1024 };

    public static async Task<ReleaseUpdate?> CheckAsync(CancellationToken cancellationToken = default, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/hailaij/LaunchDesk/releases/latest");
        request.Headers.UserAgent.ParseAdd("LaunchDesk-UpdateCheck/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await (client ?? Client).SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public static ReleaseUpdate? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)
            || !Version.TryParse(tag.TrimStart('v'), out var version))
            throw new InvalidDataException("发布版本号格式无效。");
        var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString()! : "暂无更新说明。";
        if (notes.Length > 40000) notes = notes[..40000] + "\n（更多内容请查看发布页面）";
        // Never launch a URL supplied by remote release text or metadata.
        ReleaseAsset? installer = null;
        var name = $"LaunchDesk-v{version.ToString(3)}-win-x64-setup.exe";
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var matches = assets.EnumerateArray().Where(x => x.TryGetProperty("name", out var assetName) && assetName.GetString() == name).ToArray();
            if (matches.Length == 1 && matches[0].TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes)
                && bytes is > 0 and <= UpdateDownloader.MaximumBytes
                && matches[0].TryGetProperty("digest", out var digestValue) && digestValue.ValueKind == JsonValueKind.String
                && digestValue.GetString() is { } digest && Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
                installer = new(name, bytes, digest[7..].ToUpperInvariant(),
                    new Uri("https://github.com/hailaij/LaunchDesk/releases/download/" + Uri.EscapeDataString(tag) + "/" + name));
        }
        return new(version, tag, notes, new Uri("https://github.com/hailaij/LaunchDesk/releases/tag/" + Uri.EscapeDataString(tag))) { Installer = installer };
    }
}
