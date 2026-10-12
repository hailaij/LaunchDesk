using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;
using LaunchDesk;

internal static class ResourceChecks
{
    public static int Run(string report)
    {
        var root = Environment.GetEnvironmentVariable("LAUNCHDESK_SOURCE_ROOT") ?? Environment.CurrentDirectory;
        var files = Directory.GetFiles(Path.Combine(root, "src", "LaunchDesk.App"), "*.xaml", SearchOption.AllDirectories)
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)).ToArray();
        var documents = files.Select(XDocument.Load).ToArray();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var declared = documents.SelectMany(x => x.Descendants()).Select(x => (string?)x.Attribute(xaml + "Key"))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        var keys = documents.SelectMany(x => x.Descendants().Attributes()).SelectMany(x =>
            Regex.Matches(x.Value, @"\{(?:DynamicResource|StaticResource)\s+([^{}]+)\}").Select(m => m.Groups[1].Value.Trim().Trim('\'', '"')))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (files.Length == 0 || keys.Length == 0) throw new InvalidDataException("Empty XAML resource inventory");
        var app = new App(); app.InitializeComponent();
        var type = typeof(App).Assembly.GetType("LaunchDesk.UiText")!;
        var apply = type.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public)!;
        var english = (IDictionary)type.GetField("English", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        void Validate(IEnumerable<string> requested)
        {
            foreach (var key in requested)
            {
                if (!app.Resources.Contains(key) && !declared.Contains(key)) throw new InvalidDataException("Missing XAML key: " + key);
                if (app.Resources[key] is string text && (string.IsNullOrWhiteSpace(text) || !english.Contains(key)))
                    throw new InvalidDataException("Missing/empty localized key: " + key);
            }
        }
        foreach (var language in new[] { "zh-CN", "en-US" }) { apply.Invoke(null, [language]); Validate(keys); }
        var missingRejected = false; var emptyRejected = false;
        try { Validate(["__missing_resource_probe__"]); } catch (InvalidDataException) { missingRejected = true; }
        var localized = keys.First(english.Contains); var original = app.Resources[localized];
        try { app.Resources[localized] = ""; try { Validate([localized]); } catch (InvalidDataException) { emptyRejected = true; } }
        finally { app.Resources[localized] = original; }
        if (!missingRejected || !emptyRejected) throw new InvalidDataException("Resource gate accepted invalid fixture");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, JsonSerializer.Serialize(new { status = "passed", files = files.Length, keys = keys.Length,
            languages = new[] { "zh-CN", "en-US" }, missingRejected, emptyRejected }));
        return 0;
    }
}
