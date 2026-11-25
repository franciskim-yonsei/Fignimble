using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Adapted from Pachter Lab's Manimble (BSD-2-Clause); see the repository LICENSE.
namespace Fignimble.Windows;

/// A shell running in a Windows pseudo console (ConPTY, what Windows Terminal uses): bytes in, VT-encoded output out.
public sealed class ConPty : IDisposable
{
    IntPtr hpc;
    IntPtr process, thread;
    FileStream? input;
    FileStream? output;
    readonly object gate = new();
    bool disposed;

    /// Output from the console (UTF-8 VT text), on a background thread.
    public event Action<string>? Output;
    /// The shell ended.
    public event Action? Exited;

    public static ConPty Start(string commandLine, string cwd, IDictionary<string, string> env, short cols, short rows,
                              Action<string> output, Action exited)
    {
        var pty = new ConPty();
        pty.Output += output;
        pty.Exited += exited;
        try { pty.Open(commandLine, cwd, env, cols, rows); }
        catch { pty.Dispose(); throw; }
        return pty;
    }

    void Open(string commandLine, string cwd, IDictionary<string, string> env, short cols, short rows)
    {
        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        using var inputRead = inRead;
        input = new FileStream(inWrite, FileAccess.Write);
        if (!CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        using var outputWrite = outWrite;
        output = new FileStream(outRead, FileAccess.Read);
        var hr = CreatePseudoConsole(new COORD { X = cols, Y = rows }, inRead, outWrite, 0, out hpc);
        if (hr != 0) throw new Win32Exception(hr);
        inRead.Dispose(); outWrite.Dispose();          // the console owns its ends now

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        si.lpAttributeList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref size))
        {
            int error = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(si.lpAttributeList);
            throw new Win32Exception(error);
        }

        // the environment block: NAME=value\0 ... \0, sorted, Unicode
        var block = new StringBuilder();
        foreach (var kv in env.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) block.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
        block.Append('\0');
        var envPtr = Marshal.StringToHGlobalUni(block.ToString());
        try
        {
            if (!UpdateProcThreadAttribute(si.lpAttributeList, 0, (IntPtr)0x00020016 /* PSEUDOCONSOLE */, hpc,
                                           (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000, CREATE_UNICODE_ENVIRONMENT = 0x00000400;
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                                EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, envPtr, cwd, ref si, out var pi))
                throw new Win32Exception();
            process = pi.hProcess; thread = pi.hThread;
        }
        finally
        {
            Marshal.FreeHGlobal(envPtr);
            DeleteProcThreadAttributeList(si.lpAttributeList);
            Marshal.FreeHGlobal(si.lpAttributeList);
        }
        new Thread(ReadLoop) { IsBackground = true, Name = "conpty-read" }.Start();
        new Thread(() => { if (WaitForSingleObject(process, uint.MaxValue) == 0) Exited?.Invoke(); }) { IsBackground = true }.Start();
    }

    void ReadLoop()
    {
        var buf = new byte[16384];
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[16384 * 2];
        try
        {
            while (output != null)
            {
                int n = output.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                int c = decoder.GetChars(buf, 0, n, chars, 0);       // keeps a split multi-byte character for the next read
                if (c > 0) Output?.Invoke(new string(chars, 0, c));
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public void Write(string text)
    {
        lock (gate)
        {
            if (input == null) return;
            try { var b = Encoding.UTF8.GetBytes(text); input.Write(b, 0, b.Length); input.Flush(); } catch { }
        }
    }

    public void Resize(short cols, short rows)
    {
        if (hpc != IntPtr.Zero && cols > 0 && rows > 0) ResizePseudoConsole(hpc, new COORD { X = cols, Y = rows });
    }

    public void RequestStop()
    {
        if (process != IntPtr.Zero) TerminateProcess(process, 0);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        RequestStop();
        if (hpc != IntPtr.Zero) { ClosePseudoConsole(hpc); hpc = IntPtr.Zero; }
        lock (gate) { input?.Dispose(); input = null; }
        output?.Dispose(); output = null;
        if (process != IntPtr.Zero) { CloseHandle(process); process = IntPtr.Zero; }
        if (thread != IntPtr.Zero) { CloseHandle(thread); thread = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential)] struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)] struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attrs, int size);
    [DllImport("kernel32.dll")]
    static extern int CreatePseudoConsole(COORD size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr hpc);
    [DllImport("kernel32.dll")] static extern int ResizePseudoConsole(IntPtr hpc, COORD size);
    [DllImport("kernel32.dll")] static extern void ClosePseudoConsole(IntPtr hpc);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attr, IntPtr value, IntPtr size, IntPtr prev, IntPtr ret);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env,
                                      string cwd, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
