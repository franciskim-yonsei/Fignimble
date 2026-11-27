using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Renci.SshNet;
using Renci.SshNet.Common;

// Adapted from Pachter Lab's Manimble (BSD-2-Clause); see the repository LICENSE.
namespace Fignimble.Windows;

public sealed class TerminalPanel : Border, IDisposable
{
    readonly WebView2 web = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1b, 0x1b, 0x1e) };
    readonly TextBlock message = new() { Foreground = Brushes.Gray, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
    ConPty? pty;
    bool ready;
    short cols = 100, rows = 24;
    string? cwd, python, engineRoot, cliPath;
    string[]? sshArgv;
    InteractiveSshSession? sshSession;
    string? sshCommand;
    ShellStream? sshShell;
    bool startingSsh;
    internal static string PowerShellExe => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    bool initializing, disposed;
    int generation;
    const string PageUri = "https://terminal.fignimble/terminal.html";
    readonly System.Text.StringBuilder pending = new();
    readonly object pendingGate = new();
    System.Windows.Threading.DispatcherTimer? flush;

    public bool IsRunning => pty != null || sshShell != null || startingSsh;

    public TerminalPanel()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1b, 0x1b, 0x1e));
        var g = new Grid();
        g.Children.Add(web);
        g.Children.Add(message);
        Child = g;
        Loaded += async (_, _) => await Init();
    }

    async Task Init()
    {
        if (initializing || disposed || web.CoreWebView2 != null) return;
        initializing = true;
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fignimble", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            if (disposed) return;
            await web.EnsureCoreWebView2Async(env);
            if (disposed) return;
            var core = web.CoreWebView2!;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping("terminal.fignimble", Path.Combine(AppContext.BaseDirectory, "Terminal"),
                                                              CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting += (_, args) => args.Cancel = !string.Equals(args.Uri, PageUri, StringComparison.Ordinal);
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.WebMessageReceived += OnMessage;
            core.Navigate(PageUri);
            message.Visibility = Visibility.Collapsed;
        }
        catch (Exception e)
        {
            message.Text = "The terminal needs the Microsoft Edge WebView2 runtime: " + e.Message;
        }
        finally { initializing = false; }
    }

    public void Start(string workDir, string? pythonExe, string sourceRoot, string? cliDirectory = null)
    {
        Stop();
        cwd = workDir; python = pythonExe; engineRoot = sourceRoot; cliPath = cliDirectory;
        if (ready) Spawn();
    }

    internal void StartRemote(string workDir, string[] argv)
    {
        Stop();
        cwd = workDir;
        sshArgv = argv;
        if (ready) Spawn();
    }

    internal void StartRemote(string workDir, InteractiveSshSession session, string command)
    {
        Stop();
        cwd = workDir;
        sshSession = session;
        sshCommand = command;
        if (ready) Spawn();
    }

    // CreateProcessW takes a command line, not argv. Use the Windows CRT rules, not shell escaping.
    internal static string QuoteArgument(string arg) => "\"" + Regex.Replace(arg, "(\\\\*)(\"|\\z)", m =>
        new string('\\', m.Groups[1].Length * 2 + (m.Groups[2].Length > 0 ? 1 : 0)) + m.Groups[2].Value) + "\"";

    void Spawn()
    {
        if (cwd == null || !Directory.Exists(cwd)) return;
        if (sshSession is { } session)
        {
            _ = SpawnSsh(session, sshCommand!);
            return;
        }
        var env = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value ?? "", StringComparer.OrdinalIgnoreCase);
        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        string shell;
        if (sshArgv is { } args)
        {
            shell = string.Join(" ", args.Select(QuoteArgument));
        }
        else
        {
            env["FIGNIMBLE_PROJECT"] = cwd;
            if (python is not null) env["FIGNIMBLE_SHELL_PYTHON"] = python;
            if (!string.IsNullOrEmpty(cliPath) && Directory.Exists(cliPath)) env["PATH"] = cliPath + Path.PathSeparator + env.GetValueOrDefault("PATH");
            env["PYTHONPATH"] = engineRoot + (env.TryGetValue("PYTHONPATH", out string? prior) && prior.Length > 0 ? Path.PathSeparator + prior : "");
            if (python is not null && Path.IsPathFullyQualified(python))
            {
                string scripts = Path.GetDirectoryName(python)!;
                env["PATH"] = scripts + Path.PathSeparator + env.GetValueOrDefault("PATH");
                if (Path.GetDirectoryName(scripts) is { } root && File.Exists(Path.Combine(root, "pyvenv.cfg")))
                    env["VIRTUAL_ENV"] = root;
            }
            shell = QuoteArgument(PowerShellExe) + " -NoLogo";
        }
        int thisRun = generation;
        try
        {
            pty = ConPty.Start(shell, cwd, env, cols, rows, s => OnOutput(thisRun, s), () => Dispatcher.BeginInvoke(() =>
            {
                if (generation != thisRun || pty is not { } ended) return;
                pty = null;
                _ = Task.Run(ended.Dispose);
                OnOutput(thisRun, "\r\n[Shell exited. Use Restart shell to open another.]\r\n");
            }));
        }
        catch (Exception e)
        {
            Send(new { type = "out", data = "Could not start the shell: " + e.Message + "\r\n" });
        }
    }

    async Task SpawnSsh(InteractiveSshSession session, string command)
    {
        int thisRun = generation;
        (uint columns, uint lines) = ((uint)cols, (uint)rows);
        startingSsh = true;
        ShellStream? shell = null;
        try
        {
            shell = await Task.Run(() => session.OpenShell(columns, lines, command));
            if (generation != thisRun) return;
            startingSsh = false;
            sshShell = shell;
            if (columns != cols || lines != rows) ResizeSsh();
            await Task.Run(() =>
            {
                using var reader = new StreamReader(shell, Encoding.UTF8, false, 4096, leaveOpen: true);
                char[] buffer = new char[4096];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    OnOutput(thisRun, new string(buffer, 0, read));
            });
            OnOutput(thisRun, "\r\n[Shell exited. Use Restart shell to open another.]\r\n");
        }
        catch (Exception e)
        {
            OnOutput(thisRun, "\r\n[SSH shell: " + e.Message + "]\r\n");
        }
        finally
        {
            if (generation == thisRun) { startingSsh = false; sshShell = null; }
            if (shell is not null) await Task.Run(shell.Dispose);
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref generation);
        cwd = python = engineRoot = cliPath = null;
        sshArgv = null;
        sshSession = null;
        sshCommand = null;
        startingSsh = false;
        if (sshShell is { } remoteShell) _ = Task.Run(remoteShell.Dispose);
        sshShell = null;
        var p = pty;
        pty = null;
        if (p is not null) { p.RequestStop(); _ = Task.Run(p.Dispose); }
        lock (pendingGate) pending.Clear();
        if (ready) Send(new { type = "clear" });
    }

    public void FocusTerminal()
    {
        web.Focus();
        Send(new { type = "focus" });
    }

    /// Types `text` into the shell (as if pasted), e.g. a command to start an agent.
    public void Type(string text)
    {
        if (pty is { } local) local.Write(text);
        else
        {
            try { sshShell?.Write(text); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or SshException) { }
        }
    }

    void ResizeSsh()
    {
        try { sshShell?.ChangeWindowSize((uint)cols, (uint)rows, 0, 0); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SshException) { }
    }

    // output arrives in many small pieces: batch it so the page gets a few messages a second, not thousands
    void OnOutput(int thisRun, string s)
    {
        lock (pendingGate)
        {
            if (Volatile.Read(ref generation) != thisRun) return;
            pending.Append(s);
        }
        Dispatcher.BeginInvoke(() =>
        {
            if (disposed || flush != null) return;
            flush = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            flush.Tick += (_, _) =>
            {
                string chunk;
                lock (pendingGate) { chunk = pending.ToString(); pending.Clear(); }
                if (chunk.Length > 0) Send(new { type = "out", data = chunk });
                else { flush!.Stop(); flush = null; }
            };
            flush.Start();
        });
    }

    void Send(object msg)
    {
        try { web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(msg)); } catch { }
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!string.Equals(e.Source, PageUri, StringComparison.Ordinal)) return;
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        JsonElement m = doc.RootElement;
        if (!m.TryGetProperty("type", out var type)) return;
        switch (type.GetString())
        {
            case "ready":
                ready = true;
                cols = (short)Math.Clamp(m.GetProperty("cols").GetInt32(), 10, 1000);
                rows = (short)Math.Clamp(m.GetProperty("rows").GetInt32(), 3, 500);
                if (!IsRunning && cwd != null) Spawn();
                break;
            case "in":
                Type(m.GetProperty("data").GetString() ?? "");
                break;
            case "resize":
                cols = (short)Math.Clamp(m.GetProperty("cols").GetInt32(), 10, 1000);
                rows = (short)Math.Clamp(m.GetProperty("rows").GetInt32(), 3, 500);
                pty?.Resize(cols, rows);
                ResizeSsh();
                break;
            case "copy":
                try { Clipboard.SetText(m.GetProperty("data").GetString() ?? ""); } catch { }
                break;
            case "paste":
                try { if (Clipboard.ContainsText()) Send(new { type = "paste", data = Clipboard.GetText() }); } catch { }
                break;
        }
    }

    public void Dispose()
    {
        disposed = true;
        Stop();
        flush?.Stop();
        web.Dispose();
    }
}
