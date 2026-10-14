using System.Globalization;

namespace LaunchDesk.Core;

public static class ExitCodeDisplay
{
    public static string Format(int code) => code == NativeProcess.ForcedExitCode
        ? "0xE04C4401" : code.ToString(CultureInfo.InvariantCulture);
}
