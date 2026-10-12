using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LaunchDesk.Core;

public sealed class NativeProcess : IManagedProcess
{
    public const int ForcedExitCode = unchecked((int)0xE04C4401);
    private SafeKernelHandle? job;
    private Process? process;
    public Stream Output { get; private set; } = Stream.Null;
    public Stream Error { get; private set; } = Stream.Null;
    public Stream Input { get; private set; } = Stream.Null;
    public int Id => process!.Id;
    public int ExitCode => process!.ExitCode;
    public bool ForcedTermination { get; private set; }
    private bool disposed;

    public NativeProcess(LaunchItem item)
    {
        var command = LaunchCommand.From(item);
        var childHandles = new List<SafeFileHandle>();
        var parentHandles = new List<SafeFileHandle>();
        IntPtr attributes = IntPtr.Zero, handlesMemory = IntPtr.Zero, environment = IntPtr.Zero;
        var pi = new ProcessInformation();
        try
        {
            job = new SafeKernelHandle(CreateJobObject(IntPtr.Zero, null));
            Check(!job.IsInvalid);
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
            Check(SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()));
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = true };
            Check(CreatePipe(out var outRead, out var outWrite, ref security, 0));
            parentHandles.Add(outRead); childHandles.Add(outWrite);
            Check(CreatePipe(out var errRead, out var errWrite, ref security, 0));
            parentHandles.Add(errRead); childHandles.Add(errWrite);
            Check(CreatePipe(out var inRead, out var inWrite, ref security, 0));
            childHandles.Add(inRead); parentHandles.Add(inWrite);
            foreach (var handle in parentHandles) Check(SetHandleInformation(handle, 1, 0));

            nuint size = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal((nint)size);
            Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref size));
            handlesMemory = Marshal.AllocHGlobal(IntPtr.Size * 3);
            var handles = new[] { inRead.DangerousGetHandle(), outWrite.DangerousGetHandle(), errWrite.DangerousGetHandle() };
            for (var i = 0; i < handles.Length; i++) Marshal.WriteIntPtr(handlesMemory, i * IntPtr.Size, handles[i]);
            Check(UpdateProcThreadAttribute(attributes, 0, (nuint)0x20002, handlesMemory, (nuint)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero));
            var startup = new StartupInfoEx
            {
                Info = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x101,
                    ShowWindow = (short)(item.ShowWindow ? 1 : 0),
                    Input = inRead.DangerousGetHandle(), Output = outWrite.DangerousGetHandle(), Error = errWrite.DangerousGetHandle()
                }, Attributes = attributes
            };
            var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry pair in System.Environment.GetEnvironmentVariables())
                env[(string)pair.Key] = (string)pair.Value!;
            foreach (var pair in item.Environment) env[pair.Key] = pair.Value;
            environment = Marshal.StringToHGlobalUni(string.Join('\0', env.Select(x => x.Key + "=" + x.Value)) + "\0\0");
            // Assign the suspended root before it can spawn children. Only its three pipe handles are inherited.
            Check(CreateProcess(command.FileName, new StringBuilder(LaunchCommand.Quote(command.FileName) + " " + command.Arguments),
                IntPtr.Zero, IntPtr.Zero, true, 0x4 | 0x10 | 0x400 | 0x80000, environment, command.Directory, ref startup, out pi));
            Check(AssignProcessToJobObject(job, pi.Process));
            process = Process.GetProcessById((int)pi.Id);
            Output = new FileStream(outRead, FileAccess.Read);
            Error = new FileStream(errRead, FileAccess.Read);
            Input = new FileStream(inWrite, FileAccess.Write);
            Check(ResumeThread(pi.Thread) != uint.MaxValue);
        }
        catch
        {
            if (pi.Process != IntPtr.Zero) TerminateProcess(pi.Process, 1);
            Dispose();
            foreach (var handle in parentHandles) handle.Dispose();
            throw;
        }
        finally
        {
            foreach (var handle in childHandles) handle.Dispose();
            if (pi.Thread != IntPtr.Zero) CloseHandle(pi.Thread);
            if (pi.Process != IntPtr.Zero) CloseHandle(pi.Process);
            if (attributes != IntPtr.Zero) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
            if (handlesMemory != IntPtr.Zero) Marshal.FreeHGlobal(handlesMemory);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
    }
    public async Task WaitForExitAsync()
    {
        await process!.WaitForExitAsync().ConfigureAwait(false);
        while (true)
        {
            Check(QueryInformationJobObject(job!, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero));
            if (accounting.ActiveProcesses == 0) return;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }
    public void Kill()
    {
        if (job is { IsClosed: false, IsInvalid: false })
        { Check(TerminateJobObject(job, unchecked((uint)ForcedExitCode))); ForcedTermination = true; }
    }
    public void CloseInput() { try { Input.Dispose(); } catch (IOException) { } }
    public Task SignalAsync(Func<int, Task> fallback) => fallback(Id);
    public static bool SendControlSignal(int pid) => SendControlSignal(pid, out _);
    public static bool SendControlSignal(int pid, out string detail)
    {
        detail = "";
        if (!AttachConsole((uint)pid)) { detail = ControlError("AttachConsole"); return false; }
        try
        {
            if (!SetConsoleCtrlHandler(IntPtr.Zero, true)) { detail = ControlError("SetConsoleCtrlHandler"); return false; }
            if (!GenerateConsoleCtrlEvent(0, 0)) { detail = ControlError("GenerateConsoleCtrlEvent"); return false; }
            return true;
        }
        finally { FreeConsole(); }
    }
    private static string ControlError(string operation)
    { var code = Marshal.GetLastWin32Error(); return $"{operation} Win32={code}: {new Win32Exception(code).Message}"; }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        job?.Dispose(); Input.Dispose(); Output.Dispose(); Error.Dispose(); process?.Dispose();
    }
    private static void Check(bool result) { if (!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelHandle() : this(IntPtr.Zero) { }
        public SafeKernelHandle(IntPtr value) : base(true) { SetHandle(value); }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint Id, ThreadId; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public IntPtr Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedSize; public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long ProcessTime, JobTime; public uint LimitFlags; public nuint MinWorking, MaxWorking;
        public uint ActiveProcessLimit; public nuint Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeKernelHandle job, int kind, ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(SafeKernelHandle job, int kind, out Accounting info, uint length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeKernelHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(SafeKernelHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes security, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
}
