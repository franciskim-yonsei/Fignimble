using System.Diagnostics;
using System.Text;
using Fignimble.Windows;
using Renci.SshNet.Common;

var endpoint = new SshEndpoint("127.0.0.1", int.Parse(args[0]), "researcher");
var fingerprint = args[1];
var python = args[2];
int prompts = 0;
bool trusted = false;
bool Verify(HostKeyEventArgs key) => trusted = key.FingerPrintSHA256 == fingerprint;

using var password = InteractiveSshSession.Connect("fixture", endpoint, "test-password", Verify,
    _ => throw new Exception("Password login unexpectedly fell back to keyboard prompts."));
if (!trusted || !password.IsConnected) throw new Exception("Password login failed.");

trusted = false;
using var session = InteractiveSshSession.Connect("fixture", endpoint, "", Verify, request =>
{
    var prompt = request.Prompts.Single();
    if (!trusted || prompt.IsEchoed || prompt.Request != "Verification code: ")
        throw new Exception("The verification prompt or the host key was different from expected.");
    prompt.Response = "31337";
    Interlocked.Increment(ref prompts);
});

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var data = Enumerable.Range(0, 180000).Select(i => (byte)(i % 256)).ToArray();
await Task.WhenAll(Relay("echo-input", data, 0), Relay("fail-fast", "out\0\n"u8.ToArray(), 23));
using (var shell = session.OpenShell(80, 24, "project-shell"))
{
    var first = await Task.Run(shell.ReadLine, timeout.Token).WaitAsync(timeout.Token);
    if (first != "project shell") throw new Exception($"Unexpected shell response: {first}");
    shell.ChangeWindowSize(100, 35, 0, 0);
    shell.Write("pwd\r");
    var reply = await Task.Run(shell.ReadLine, timeout.Token).WaitAsync(timeout.Token);
    if (reply != "/work/project") throw new Exception($"Unexpected shell directory: {reply}");
}
if (prompts != 1) throw new Exception($"Expected one verification prompt, observed {prompts}.");
Console.WriteLine("Verified password and keyboard-interactive sign-in, concurrent SSH commands and a terminal on one connection.");

async Task Relay(string command, byte[] output, int status)
{
    var start = new ProcessStartInfo(python)
    {
        UseShellExecute = false, RedirectStandardInput = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (var arg in new[] { "-u", "-m", "fignimble.ssh_pipe", "-o", "BatchMode=yes", "-T", "fixture", "--", command })
        start.ArgumentList.Add(arg);
    start.Environment["FIGNIMBLE_SSH_SESSION_HOST"] = session.Host;
    start.Environment["FIGNIMBLE_SSH_PIPE"] = session.PipeName;
    using var process = Process.Start(start)!;
    try
    {
        using var stdout = new MemoryStream();
        var read = process.StandardOutput.BaseStream.CopyToAsync(stdout, timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        if (command == "echo-input") await process.StandardInput.BaseStream.WriteAsync(output, timeout.Token);
        process.StandardInput.Close();
        await Task.WhenAll(read, process.WaitForExitAsync(timeout.Token));
        var error = await stderr;
        if (process.ExitCode != status || !stdout.ToArray().SequenceEqual(output) || error != "server stderr\r\n")
            throw new Exception($"{command}: exit {process.ExitCode}, {stdout.Length} bytes; {error}");
    }
    finally
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
}
