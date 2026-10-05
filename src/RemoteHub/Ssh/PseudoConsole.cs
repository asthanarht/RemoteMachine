using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace RemoteHub.Ssh;

public sealed class PseudoConsole : IAsyncDisposable
{
    private readonly Channel<byte[]> _output = Channel.CreateBounded<byte[]>(32);
    private readonly CancellationTokenSource _discardOutput = new();
    private readonly SemaphoreSlim _inputGate = new(1);
    private readonly object _lifetime = new();
    private FileStream? _input;
    private FileStream? _read;
    private SafeFileHandle? _process;
    private SafeFileHandle? _job;
    private IntPtr _console;
    private Task _pump = Task.CompletedTask;
    private Task? _cleanup;
    public Task<int> ExitCode { get; private set; } = Task.FromResult(0);
    public ChannelReader<byte[]> Output => _output.Reader;

    public static PseudoConsole Start(string executable, IReadOnlyList<string> arguments, int columns, int rows)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("The terminal executable is not installed.", executable);
        var result = new PseudoConsole();
        try { result.Initialize(executable, arguments, columns, rows); return result; }
        catch
        {
            // Setup can fail after creating a suspended child. Cleanup still owns its exact process handle.
            _ = CleanupFailedStart(result);
            throw;
        }
        static async Task CleanupFailedStart(PseudoConsole console)
        {
            try { await console.DisposeAsync(); }
            catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
            { Services.AppLog.Write("ssh-startup-cleanup-failed", details: new { error.Message }); }
        }
    }

    private void Initialize(string executable, IReadOnlyList<string> arguments, int columns, int rows)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw Error("Cannot create terminal input.");
        using var inputSide = inputRead;
        _input = new FileStream(inputWrite, FileAccess.Write, 8192, isAsync: false);
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0)) throw Error("Cannot create terminal output.");
        using var outputSide = outputWrite;
        _read = new FileStream(outputRead, FileAccess.Read, 8192, isAsync: false);
        Marshal.ThrowExceptionForHR(CreatePseudoConsole(Size(columns, rows), inputSide, outputSide, 0, out _console));
        _pump = PumpOutput();
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job.IsInvalid) throw Error("Cannot create the terminal process group.");
        var limits = new JobLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (!SetInformationJobObject(_job, 9, ref limits, (uint)Marshal.SizeOf<JobLimits>())) throw Error("Cannot protect terminal process lifetime.");

        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        if (size == IntPtr.Zero) throw Error("Cannot size terminal process attributes.");
        IntPtr attributes = Marshal.AllocHGlobal(size);
        bool initialized = false;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw Error("Cannot initialize terminal process attributes.");
            initialized = true;
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x00020016, _console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw Error("Cannot attach the terminal console.");
            // Null standard handles prevent a console host's redirected streams from bypassing ConPTY.
            var startup = new StartupInfoEx { Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100 }, Attributes = attributes };
            if (!CreateProcess(executable, new StringBuilder(SshCommand.CommandLine(executable, arguments)), IntPtr.Zero, IntPtr.Zero,
                false, 0x00080000 | 0x00000400 | 0x00000004, IntPtr.Zero,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ref startup, out var process))
                throw Error("Cannot start the terminal process.");
            _process = new SafeFileHandle(process.Process, true);
            using var thread = new SafeFileHandle(process.Thread, true);
            var processHandle = _process;
            ExitCode = Task.Run(() =>
            {
                if (WaitForSingleObject(processHandle, uint.MaxValue) != 0) throw Error("Cannot wait for the terminal process.");
                if (!GetExitCodeProcess(processHandle, out uint code)) throw Error("Cannot read the terminal exit code.");
                return unchecked((int)code);
            });
            if (!AssignProcessToJobObject(_job, _process)) throw Error("Cannot contain the terminal process.");
            if (ResumeThread(thread) == uint.MaxValue) throw Error("Cannot resume the terminal process.");
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes);
        }
    }

    private async Task PumpOutput()
    {
        Exception? failure = null;
        try
        {
            var buffer = new byte[8192];
            while (_read is not null)
            {
                int count = await _read.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (count == 0) break;
                if (_discardOutput.IsCancellationRequested) continue;
                try { await _output.Writer.WriteAsync(buffer[..count], _discardOutput.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_discardOutput.IsCancellationRequested) { }
            }
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 109 or 232) { }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { failure = error; }
        finally { _output.Writer.TryComplete(failure); }
    }

    public async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        await _inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_input is null || ExitCode.IsCompleted) return;
            await _input.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _inputGate.Release(); }
    }

    public void Resize(int columns, int rows)
    {
        lock (_lifetime)
        {
            if (_console == IntPtr.Zero || _cleanup is not null) return;
            Marshal.ThrowExceptionForHR(ResizePseudoConsole(_console, Size(columns, rows)));
        }
    }

    public Task FinishAsync(bool terminate = false)
    {
        lock (_lifetime)
        {
            if (terminate)
            {
                _discardOutput.Cancel();
                if (_process is { IsClosed: false } && !ExitCode.IsCompleted && !TerminateProcess(_process, 1) &&
                    Marshal.GetLastWin32Error() != 5) throw Error("Cannot stop the terminal process.");
                _job?.Dispose();
            }
            return _cleanup ??= Cleanup();
        }
    }

    private async Task Cleanup()
    {
        // ConPTY may write final output while closing; keep the pipe pump alive until close returns.
        IntPtr console = _console;
        _console = IntPtr.Zero;
        if (_discardOutput.IsCancellationRequested) await ExitCode.ConfigureAwait(false);
        if (console != IntPtr.Zero) await Task.Run(() => ClosePseudoConsole(console)).ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        await ExitCode.ConfigureAwait(false);
        _input?.Dispose(); _read?.Dispose(); _process?.Dispose(); _job?.Dispose();
    }
    public ValueTask DisposeAsync() => new(FinishAsync(terminate: true));
    private static Coordinate Size(int columns, int rows) => new() { X = (short)Math.Clamp(columns, 2, 1000), Y = (short)Math.Clamp(rows, 1, 500) };
    private static Win32Exception Error(string message) => new(Marshal.GetLastWin32Error(), message);

    [StructLayout(LayoutKind.Sequential)] private struct Coordinate { public short X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr security, uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coordinate size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")] private static extern int ResizePseudoConsole(IntPtr console, Coordinate size);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref JobLimits information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
}
