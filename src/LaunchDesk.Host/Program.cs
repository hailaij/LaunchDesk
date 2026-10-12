using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using LaunchDesk.Core;
using Microsoft.Win32;

namespace LaunchDesk.Host;

internal static class Program
{
    internal const string ServiceName = "LaunchDesk.Managed";
    internal static readonly string BinaryRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LaunchDesk", "Service");
    internal static readonly string DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LaunchDesk", "Service");
    [STAThread]
    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            if (args.Length == 2 && args[0] == "--signal")
                return NativeProcess.SendControlSignal(int.Parse(args[1]), out _) ? 0 : 1;
            if (args.Length == 5 && args[0] == "--elevated")
            {
                ElevationBroker.RunHelperAsync(args[1], args[2], int.Parse(args[3]), long.Parse(args[4]), SignalAsync).GetAwaiter().GetResult();
                return 0;
            }
            if (args.SequenceEqual(new[] { "--service" })) { ServiceBase.Run(new ManagedService()); return 0; }
            if (args.Length < 2 || args[0] != "service") throw new ArgumentException("service install/configure <config.json> | start | stop | uninstall | status");
            if (args[1] == "status")
            {
                using var service = new ServiceController(ServiceName);
                try { Console.WriteLine(service.Status); }
                catch (InvalidOperationException error) when (error.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 1060 }) { Console.WriteLine("NotInstalled"); }
                return 0;
            }
            if (!ElevationBroker.IsAdministrator()) throw new UnauthorizedAccessException("This operation requires administrator consent.");
            Execute(args);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (args.Contains("--interactive")) MessageBox(IntPtr.Zero, error.Message, "LaunchDesk", 0x10);
            return 1;
        }
    }
    private static void Execute(string[] args)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName);
        var registered = key is not null;
        var expected = '"' + Path.Combine(BinaryRoot, "LaunchDesk.Host.exe") + "\" --service";
        if (registered && !string.Equals(key!.GetValue("ImagePath") as string, expected, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The existing service is not owned by this LaunchDesk installation.");
        if (args[1] is "install" or "configure")
        {
            if (args.Length < 3 || args[2].StartsWith("--")) throw new ArgumentException("Select an exported configuration JSON file.");
            // Import reads exactly the selected file and enforces the normal size and schema limits.
            var configuration = ConfigurationStore.Read(args[2]);
            ServicePolicy.Validate(configuration);
            if (args[1] == "install" && registered) throw new InvalidOperationException("Service is already installed. Use configure instead.");
            if (args[1] == "configure" && !registered) throw new InvalidOperationException("Service is not installed.");
            if (registered)
            {
                using var service = new ServiceController(ServiceName);
                if (service.Status != ServiceControllerStatus.Stopped) throw new InvalidOperationException("Stop the service before replacing its configuration.");
            }
            if (args[1] == "configure")
            { SaveServiceConfiguration(configuration); return; }
            ProtectDirectory(Path.GetDirectoryName(BinaryRoot)!, false);
            ProtectDirectory(BinaryRoot, false);
            if (Directory.EnumerateFileSystemEntries(BinaryRoot).Any()) throw new IOException("Service runtime directory must be empty before installation. Retained files require administrator review/removal.");
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory))
            {
                if (Path.GetExtension(file) is not (".exe" or ".dll" or ".json")) continue;
                if (Path.GetExtension(file) == ".json" && !file.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked runtime files are not allowed.");
                var destination = Path.Combine(BinaryRoot, Path.GetFileName(file));
                if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked destination files are not allowed.");
                using (var input = File.OpenRead(file))
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                var permissions = new FileSecurity(); permissions.SetAccessRuleProtection(false, false);
                new FileInfo(destination).SetAccessControl(permissions);
            }
            foreach (var name in new[] { "LICENSE", "THIRD-PARTY-NOTICES.md" })
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, name))) File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(BinaryRoot, name), false);
            var notices = Path.Combine(AppContext.BaseDirectory, "runtime-notices");
            if (Directory.Exists(notices))
            {
                if ((File.GetAttributes(notices) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked license directory is not allowed.");
                var destination = Directory.CreateDirectory(Path.Combine(BinaryRoot, "runtime-notices"));
                foreach (var file in Directory.EnumerateFiles(notices))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked license file is not allowed.");
                    File.Copy(file, Path.Combine(destination.FullName, Path.GetFileName(file)), false);
                }
            }
            SaveServiceConfiguration(configuration);
            Sc("create", ServiceName, "binPath=", expected, "obj=", @"NT AUTHORITY\LocalService", "start=", "demand", "DisplayName=", "LaunchDesk Managed Services");
            return;
        }
        if (!registered) throw new InvalidOperationException("Service is not installed.");
        using var controller = new ServiceController(ServiceName);
        switch (args[1])
        {
            case "start": controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); break;
            case "stop": Stop(controller); break;
            case "uninstall": Stop(controller); Sc("delete", ServiceName); break;
            default: throw new ArgumentException("Unknown service operation.");
        }
    }
    private static void SaveServiceConfiguration(Configuration configuration)
    {
        ProtectDirectory(Path.GetDirectoryName(DataRoot)!, false);
        ProtectDirectory(DataRoot, true);
        new ConfigurationStore(DataRoot).Save(configuration);
    }
    private static void Stop(ServiceController service)
    {
        if (service.Status == ServiceControllerStatus.Stopped) return;
        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(5));
    }
    private static void Sc(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit(); Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new IOException(output.Result + error.Result);
    }
    private static void ProtectDirectory(string path, bool writable)
    {
        for (var parent = new DirectoryInfo(path); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Service paths must not contain filesystem links.");
        var directory = Directory.CreateDirectory(path);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid, WellKnownSidType.LocalServiceSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), sid == WellKnownSidType.LocalServiceSid
                ? writable ? FileSystemRights.Modify : FileSystemRights.ReadAndExecute : FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
        // Reject links before accessing existing data; reset explicit child permissions as well.
        foreach (var child in directory.EnumerateFileSystemInfos())
        {
            if ((child.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Service paths must not contain filesystem links.");
            if (child is DirectoryInfo nested) ProtectDirectory(nested.FullName, writable);
            else { var inherited = new FileSecurity(); inherited.SetAccessRuleProtection(false, false); inherited.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)); ((FileInfo)child).SetAccessControl(inherited); }
        }
    }
    internal static async Task SignalAsync(int pid)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--signal"); info.ArgumentList.Add(pid.ToString());
        using var helper = Process.Start(info)!;
        try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { if (!helper.HasExited) helper.Kill(); await helper.WaitForExitAsync(); throw; }
        if (helper.ExitCode != 0) throw new IOException("Unable to signal the managed process.");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr owner, string text, string caption, uint flags);
}

internal sealed class ManagedService : ServiceBase
{
    private readonly string dataRoot;
    private ProcessManager? manager;
    private Task? startup;
    private readonly CancellationTokenSource stopping = new();
    public ManagedService() : this(Program.DataRoot) { }
    internal ManagedService(string root) { dataRoot = root; ServiceName = Program.ServiceName; AutoLog = false; CanStop = true; CanShutdown = true; }
    protected override void OnStart(string[] args)
    {
        try
        {
            var configuration = new ConfigurationStore(dataRoot).Load();
            ServicePolicy.Validate(configuration);
            manager = new ProcessManager(configuration, dataRoot, Program.SignalAsync);
            startup = StartAsync();
        }
        catch (Exception error)
        {
            new ErrorDiagnostics(dataRoot).Write("service-start", error);
            ExitCode = 1;
            throw;
        }
    }
    private async Task StartAsync()
    {
        try { await manager!.StartAllAsync(cancellationToken: stopping.Token); }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception error) { new ErrorDiagnostics(dataRoot).Write("service-start", error); ExitCode = 1; _ = Task.Run(Stop); }
    }
    protected override void OnStop()
    {
        stopping.Cancel();
        var cleanup = CleanupAsync();
        while (!cleanup.Wait(TimeSpan.FromSeconds(5))) RequestAdditionalTime(10000);
        cleanup.GetAwaiter().GetResult();
    }
    private async Task CleanupAsync()
    { if (manager is not null) await manager.DisposeAsync(); if (startup is not null) await startup; }
    protected override void OnShutdown()
    {
        stopping.Cancel();
        // SCM shutdown is not a pending Stop callback, so RequestAdditionalTime is not valid here.
        var cleanup = CleanupAsync();
        if (!cleanup.Wait(TimeSpan.FromSeconds(15)))
            new ErrorDiagnostics(dataRoot).Write("service-shutdown", new System.TimeoutException("Shutdown cleanup is still pending; Windows owns the remaining shutdown deadline."));
    }
}
