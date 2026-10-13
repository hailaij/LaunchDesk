using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaunchDesk.Core;

public sealed record ReleaseUpdate(Version Version, string Tag, string Notes, Uri Page)
{
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
        var page = ReleasePage(tag);
        var version = Version.Parse(tag.TrimStart('v'));
        var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString()! : "暂无更新说明。";
        if (notes.Length > 40000) notes = notes[..40000] + "\n（更多内容请查看发布页面）";
        // Never launch a URL supplied by remote release text or metadata.
        return new(version, tag, notes, page);
    }
    public static Uri ReleasePage(string tag)
    {
        if (!Regex.IsMatch(tag, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)
            || !Version.TryParse(tag.TrimStart('v'), out _))
            throw new InvalidDataException("发布版本号格式无效。");
        return new Uri("https://github.com/hailaij/LaunchDesk/releases/tag/" + Uri.EscapeDataString(tag));
    }
}
