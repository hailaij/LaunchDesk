using System.Reflection.PortableExecutable;

namespace LaunchDesk.Core;

public static class ServicePolicy
{
    public static void Validate(Configuration configuration)
    {
        ConfigurationStore.Validate(configuration);
        foreach (var item in configuration.Items)
        {
            if (item.RequireElevation || item.ShowWindow)
                throw new InvalidDataException("Windows 服务不支持按项提权或显示窗口：" + item.Name);
            if (item.Kind == LaunchKind.Executable)
            {
                using var stream = File.OpenRead(LaunchCommand.From(item).FileName);
                using var pe = new PEReader(stream);
                if (pe.PEHeaders.PEHeader?.Subsystem != Subsystem.WindowsCui)
                    throw new InvalidDataException("Windows 服务只支持后台控制台程序：" + item.Name);
            }
        }
    }
}
