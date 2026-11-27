using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Fignimble.Windows;

internal sealed record SshEndpoint(string Host, int Port, string User)
{
    public string Key => $"[{Host.ToLowerInvariant()}]:{Port}";

    public static async Task<SshEndpoint> Resolve(string destination)
    {
        string native = Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe");
        var start = new ProcessStartInfo(File.Exists(native) ? native : "ssh.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-G", "--", destination }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not read OpenSSH settings.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("OpenSSH took too long to read its settings.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException((await stderr).Trim());
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in (await stdout).Split('\n'))
        {
            int space = line.IndexOf(' ');
            if (space > 0) options.TryAdd(line[..space], line[(space + 1)..].Trim());
        }
        if (new[] { "proxyjump", "proxycommand" }.Any(key => options.TryGetValue(key, out var value) && !value.Equals("none", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Interactive sign-in supports direct SSH connections. Use OpenSSH key or agent login for hosts reached through a jump host or proxy.");
        if (!options.TryGetValue("hostname", out var host) || !options.TryGetValue("user", out var user) ||
            !int.TryParse(options.GetValueOrDefault("port"), out int port) || port is < 1 or > 65535)
            throw new InvalidOperationException("OpenSSH did not return a valid host, user, and port.");
        return new SshEndpoint(host, port, user);
    }
}

internal sealed class InteractiveSshSession : IDisposable
{
    private const int BlockSize = 65536;
    private readonly SshClient client;
    private readonly CancellationTokenSource stop = new();
    private readonly Task listener;

    private InteractiveSshSession(string host, SshClient client)
    {
        Host = host;
        this.client = client;
        listener = Listen();
    }

    public string Host { get; }
    public string PipeName { get; } = "fignimble-ssh-" + Guid.NewGuid().ToString("N");
    public bool IsConnected => !stop.IsCancellationRequested && client.IsConnected;

    public static InteractiveSshSession Connect(string host, SshEndpoint endpoint, string password,
        Func<HostKeyEventArgs, bool> trust, Action<AuthenticationPromptEventArgs> prompt)
    {
        var keyboard = new KeyboardInteractiveAuthenticationMethod(endpoint.User);
        keyboard.AuthenticationPrompt += (_, args) => prompt(args);
        AuthenticationMethod[] methods = password.Length == 0
            ? [keyboard] : [new PasswordAuthenticationMethod(endpoint.User, password), keyboard];
        var info = new ConnectionInfo(endpoint.Host, endpoint.Port, endpoint.User, methods)
            { Timeout = TimeSpan.FromSeconds(30) };
        var client = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(20) };
        string? verifiedKey = null;
        client.HostKeyReceived += (_, args) =>
        {
            args.CanTrust = verifiedKey is null ? trust(args) : verifiedKey == args.FingerPrintSHA256;
            if (args.CanTrust) verifiedKey = args.FingerPrintSHA256;
        };
        try
        {
            client.Connect();
            return new InteractiveSshSession(host, client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public ShellStream OpenShell(uint columns, uint rows, string command)
    {
        var shell = client.CreateShellStream("xterm-256color", columns, rows, 0, 0, 16384);
        try { shell.WriteLine("exec " + command); return shell; }
        catch { shell.Dispose(); throw; }
    }

    // The engine opens one pipe per remote command. The pipe is restricted to this Windows user.
    private async Task Listen()
    {
        while (!stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { pipe.Dispose(); return; }
            catch { pipe.Dispose(); throw; }
            _ = Task.Run(() => Relay(pipe));
        }
    }

    // The first input frame is the command, followed by input bytes and a zero-length EOF frame.
    // Output frames carry stdout (1), stderr (2), or the process exit status (3).
    private async Task Relay(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        using (var send = new SemaphoreSlim(1))
        {
            var token = cancellation.Token;
            Task input = Task.CompletedTask, stdout = Task.CompletedTask, stderr = Task.CompletedTask;
            try
            {
                using var command = client.CreateCommand(Encoding.UTF8.GetString(await ReadFrame(pipe, token).ConfigureAwait(false)));
                Task executing = command.ExecuteAsync(token);
                input = RelayInput(pipe, command, executing, token);
                stdout = RelayOutput(command.OutputStream, 1, pipe, send, token);
                stderr = RelayOutput(command.ExtendedOutputStream, 2, pipe, send, token);
                await await Task.WhenAny(executing, input, stdout, stderr).ConfigureAwait(false);
                await executing.ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                byte[] status = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(status, command.ExitStatus ?? 255);
                await WriteFrame(pipe, send, 3, status, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                try { await WriteFrame(pipe, send, 2, Encoding.UTF8.GetBytes(ex.Message + "\n"), token).ConfigureAwait(false); }
                catch (Exception failure) when (failure is IOException or OperationCanceledException) { }
            }
            catch (Exception) when (token.IsCancellationRequested) { }
            finally
            {
                cancellation.Cancel();
                try { await Task.WhenAll(input, stdout, stderr).ConfigureAwait(false); }
                catch (Exception) { /* The pipe or the SSH channel has already closed. */ }
            }
        }
    }

    private static async Task<byte[]> ReadFrame(Stream pipe, CancellationToken token)
    {
        byte[] header = new byte[4];
        await pipe.ReadExactlyAsync(header, token).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is < 0 or > BlockSize) throw new InvalidDataException("Invalid SSH pipe frame size.");
        byte[] bytes = new byte[size];
        await pipe.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    private static async Task RelayInput(Stream pipe, SshCommand command, Task executing, CancellationToken token)
    {
        byte[] bytes = await ReadFrame(pipe, token).ConfigureAwait(false);
        try
        {
            if (executing.IsCompleted) return;
            using var input = command.CreateInputStream();
            while (bytes.Length > 0)
            {
                await input.WriteAsync(bytes, token).ConfigureAwait(false);
                bytes = await ReadFrame(pipe, token).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException) when (executing.IsCompletedSuccessfully) { return; }
        // A disconnected pipe cancels even a command whose input has finished.
        await pipe.ReadExactlyAsync(new byte[1], token).ConfigureAwait(false);
        throw new InvalidDataException("Unexpected SSH pipe data after EOF.");
    }

    private static async Task RelayOutput(Stream stream, byte kind, Stream pipe, SemaphoreSlim send, CancellationToken token)
    {
        byte[] bytes = new byte[BlockSize];
        int count;
        while ((count = await stream.ReadAsync(bytes, token).ConfigureAwait(false)) > 0)
            await WriteFrame(pipe, send, kind, bytes.AsMemory(0, count), token).ConfigureAwait(false);
    }

    private static async Task WriteFrame(Stream pipe, SemaphoreSlim send, byte kind, ReadOnlyMemory<byte> data, CancellationToken token)
    {
        await send.WaitAsync(token).ConfigureAwait(false);
        try
        {
            byte[] header = new byte[5];
            header[0] = kind;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), data.Length);
            await pipe.WriteAsync(header, token).ConfigureAwait(false);
            await pipe.WriteAsync(data, token).ConfigureAwait(false);
        }
        finally { send.Release(); }
    }

    public void Dispose()
    {
        stop.Cancel();
        client.Dispose();
        _ = listener.Exception;
    }
}
