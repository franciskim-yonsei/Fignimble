using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Fignimble.Windows;

internal static class ManagedPython
{
    // Official CPython installers from python.org. Keep the version and both hashes in sync.
    private const string Version = "3.13.17";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task<string> SetUp(string settings, string? existing)
    {
        string runtime = Path.Combine(settings, "python-runtime", Version);
        string python = existing ?? Path.Combine(runtime, "python.exe");
        if (existing is null && !File.Exists(python))
        {
            bool arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            string arch = arm ? "arm64" : "amd64";
            string hash = arm ? "3089a1977c32b26f5078e5a7d0285b7a861b8c96250c5dac3b6af59fbe9e9a75"
                              : "5da012101ba2e17113a9a1cd901e9ce1441086f2b7ecb5de06ec94163899a190";
            string installer = Path.Combine(Path.GetTempPath(), "fignimble-python-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                var bytes = await Http.GetByteArrayAsync($"https://www.python.org/ftp/python/{Version}/python-{Version}-{arch}.exe");
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The Python installer did not match the expected checksum.");
                await File.WriteAllBytesAsync(installer, bytes);
                await Run(installer, "/quiet", "InstallAllUsers=0", "PrependPath=0", "Include_launcher=0", "Include_test=0",
                    "Include_doc=0", "Include_tcltk=0", "Shortcuts=0", "AssociateFiles=0", "TargetDir=" + runtime);
            }
            finally { File.Delete(installer); }
        }

        string environment = Path.Combine(settings, "python");
        string managed = Path.Combine(environment, "Scripts", "python.exe");
        if (!File.Exists(managed)) await Run(python, "-m", "venv", environment);
        return managed;
    }

    private static async Task Run(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Python setup.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("Python setup timed out.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"Python setup exited with code {process.ExitCode}.");
    }
}
