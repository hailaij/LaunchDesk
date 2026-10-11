using System.Text.RegularExpressions;

namespace LaunchDesk.Core;

public sealed record LogLink(int Start, int Length, Uri Uri);
public static partial class LogLinks
{
    public static IEnumerable<LogLink> Find(string text)
    {
        foreach (Match match in UrlPattern().Matches(text))
        {
            var value = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '。', '，', '；', '！', '？', '）', ']', '}', '"', '\'');
            while (value.EndsWith(')') && value.Count(x => x == ')') > value.Count(x => x == '(')) value = value[..^1];
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0)
                yield return new(match.Index, value.Length, uri);
        }
    }
    [GeneratedRegex("https?://[^\\s<>\"'\\x00-\\x1F]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
