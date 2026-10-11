using System.Windows.Media;
using Microsoft.Win32;

namespace LaunchDesk;

internal static class Theme
{
    public static bool IsDark { get; private set; }
    public static void Apply(string mode)
    {
        var dark = mode == "Dark";
        if (mode == "System")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        IsDark = dark;
        var colors = new Dictionary<string, string>
        {
            ["Canvas"] = dark ? "#202124" : "#F4F5F7",
            ["Surface"] = dark ? "#292B2F" : "#FFFFFF",
            ["SurfaceHover"] = dark ? "#35383D" : "#EBEEF1",
            ["Stroke"] = dark ? "#404349" : "#DCE1E6",
            ["Text"] = dark ? "#F1F3F5" : "#24272C",
            ["Muted"] = dark ? "#ADB4BE" : "#636B76",
            ["Selection"] = dark ? "#24483D" : "#E2F2EB",
            ["Accent"] = dark ? "#218563" : "#13795B",
            ["Terminal"] = dark ? "#17191C" : "#202225",
            ["TerminalText"] = "#E3E8ED"
        };
        foreach (var (name, color) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze(); System.Windows.Application.Current.Resources[name] = brush;
        }
    }
}
