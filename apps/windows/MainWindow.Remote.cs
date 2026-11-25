using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private sealed record SshHosts(string[] Hosts);
    private sealed record SshListing(string Path, SshEntry[] Entries);
    private sealed record SshEntry(string Name, bool Dir);

    private void RemoteClicked(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = RemoteButton };
        void Add(string label, Func<Task> run, bool enabled = true)
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, IsEnabled = enabled };
            item.Click += async (_, _) => await run();
            menu.Items.Add(item);
        }
        Add("Open remote project…", ChooseRemoteProject);
        Add("Test login in an SSH console…", ChooseSshConsole);
        Add("Import Python panel from server…", ChooseRemotePanel, validFigure);
        Add("Refresh imported remote panel", RefreshRemotePanel, validFigure && Page.Selection?.Kind == "panel");
        if (engine?.Remote is { } remote)
            Add($"Reconnect to {remote.Host}", () => OpenRemote(remote.Host, remote.Path, remote.Python));
        menu.IsOpen = true;
    }

    private async Task<string[]> RemoteHosts()
    {
        string[] aliases = [];
        await During("Reading SSH config…", async () =>
        {
            aliases = (await (engine is { Running: true } client ? client : GetEngine())
                .CallAsync<SshHosts>("remote_hosts")).Hosts;
            Status.Text = "Choose an SSH host.";
        });
        return aliases;
    }

    private static bool ValidHost(string host) => host.Length is > 0 and <= 240 && !host.StartsWith('-') &&
        Regex.IsMatch(host, @"\A[A-Za-z0-9_.\-@:\[\]]+\z");

    private async Task ChooseSshConsole()
    {
        var aliases = await RemoteHosts();
        var dialog = new FormDialog(this, "Test an SSH login");
        dialog.Note("The separate OpenSSH console handles interactive prompts and host-key verification. The app’s project connection separately requires a key or agent; it does not remember credentials entered here.");
        var input = dialog.Text("Host alias or user@host", engine?.Remote?.Host ?? aliases.FirstOrDefault() ?? "");
        if (!dialog.Confirm("Open SSH console", () => !ValidHost(input.Text.Trim()) ? "Enter a valid SSH host or alias." : null)) return;
        try
        {
            string native = System.IO.Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe");
            var start = new ProcessStartInfo(File.Exists(native) ? native : "ssh.exe") { UseShellExecute = true };
            start.ArgumentList.Add(input.Text.Trim());
            using var proc = Process.Start(start);
            Status.Text = "Opened an SSH console for login testing.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async Task ChooseRemoteProject()
    {
        var aliases = await RemoteHosts();
        var dialog = new FormDialog(this, "Open project over SSH");
        dialog.Note("Use a trusted Linux host where SSH key or agent login is already configured (test ssh in a terminal first). The selected Python must have the Fignimble engine dependencies. Changes are mirrored back to the server.");
        var host = dialog.Text("Host alias or user@host", aliases.FirstOrDefault() ?? "");
        var path = dialog.Text("Remote project directory", "~/");
        var python = dialog.Text("Python on the server", "python3");
        if (dialog.Confirm("Connect", () => !ValidHost(host.Text.Trim()) ? "Enter a valid SSH host or alias." :
            string.IsNullOrWhiteSpace(path.Text) || string.IsNullOrWhiteSpace(python.Text) ? "Enter a project directory and Python." : null))
            await OpenRemote(host.Text.Trim(), path.Text.Trim(), python.Text.Trim());
    }

    private Task OpenRemote(string host, string path, string python) => During("Connecting over SSH…", async () =>
    {
        if (!ValidHost(host)) throw new ArgumentException("Invalid SSH host.");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host + "\n" + path)))[..24].ToLowerInvariant();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string mirror = System.IO.Path.Combine(local, "Fignimble", "remote", hash);
        Directory.CreateDirectory(mirror);
        ResetProject();
        var remote = new RemoteProject(host, path, python, mirror);
        var client = SwitchEngine(EngineClient.ResolvePython(null), remote);
        await client.CallAsync<JsonElement>("open", new { project = mirror });
        project = mirror;
        Title = $"Fignimble — {host}:{path}";
        if (TerminalRequested) StartShell();
        await RefreshProject(null);
    });

    private async Task ChooseRemotePanel()
    {
        if (!validFigure || shownFigure is not { } figure) return;
        var aliases = await RemoteHosts();
        var picker = new FormDialog(this, "Find a remote panel");
        var host = picker.Text("Host alias or user@host", engine?.Remote?.Host ?? aliases.FirstOrDefault() ?? "");
        var directory = picker.Text("Directory containing the Python file", "~");
        if (!picker.Confirm("List scripts", () => ValidHost(host.Text.Trim()) ? null : "Enter a valid SSH host or alias.")) return;
        string target = host.Text.Trim();
        SshListing? listing = null;
        await During("Listing remote scripts…", async () =>
        {
            listing = await engine!.CallAsync<SshListing>("remote_list", new { host = target, path = directory.Text.Trim() });
            Status.Text = "Choose a panel to import.";
        });
        if (listing is null) return;
        string[] scripts = listing.Entries.Where(e => !e.Dir).Select(e => e.Name).ToArray();
        if (scripts.Length == 0) { Status.Text = "No Python scripts found in that directory."; return; }
        var dialog = new FormDialog(this, "Import remote panel");
        dialog.Note("The script will run in the figure’s Python environment. Its data and other dependencies are not copied.");
        var script = dialog.Choice("Python file", scripts, scripts[0]);
        if (dialog.Confirm("Import", () => null) && script.SelectedItem is string name && shownFigure == figure)
            await ImportRemotePanel(target, listing.Path.TrimEnd('/') + "/" + name);
    }

    private Task ImportRemotePanel(string host, string path) => AddPanel("import_remote", "Importing remote panel…", (figure, box) =>
        new { figure, host, path, x = box.X, y = box.Y });

    private Task RefreshRemotePanel()
    {
        if (!validFigure || shownFigure is not { } figure || Page.Selection?.Code?.File is not { } file)
            return Task.CompletedTask;
        return During("Fetching updated panel…", async () =>
        {
            var result = await engine!.CallAsync<EditResult>("update_remote", new { figure, file });
            ShowRender(result.Figure);
            await ReadState(figure);
        });
    }
}
