using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private sealed record SshHosts(string[] Hosts);
    private sealed record SshListing(string Path, SshEntry[] Entries);
    private sealed record SshEntry(string Name, bool Dir)
    {
        public override string ToString() => Dir ? Name + "/" : Name;
    }
    private sealed record RemoteOpenResult(string? RemotePath, string[]? TerminalArgv);
    private RemoteOpenResult? remoteOpen;

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
        Add("Refresh imported remote panel", RefreshRemotePanel, CanRefreshRemotePanel);
        if (engine?.Remote is { } remote)
            Add($"Reconnect to {remote.Host}", () => OpenRemote(remote.Host, remote.Path, remote.Python, shownFigure));
        menu.IsOpen = true;
    }

    private async Task<string[]> RemoteHosts()
    {
        string[] aliases = [];
        await During("Reading SSH config…", async () =>
        {
            // Listing aliases must not replace a disconnected project's SSH engine with a local one.
            using var temporary = engine is { Running: true } ? null : new EngineClient(ProjectPython(null));
            aliases = (await (temporary ?? engine!).CallAsync<SshHosts>("remote_hosts")).Hosts;
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
        string startHost = engine?.Remote?.Host ?? aliases.FirstOrDefault() ?? "";
        var host = SshHostField(dialog, aliases, startHost);
        var path = dialog.Text("Remote project directory", "~/");
        var python = dialog.Text("Python on the server", RememberedPython(startHost));
        string previousHost = startHost;
        string suggestedPython = python.Text;
        host.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            string current = host.Text.Trim();
            if (current == previousHost) return;
            if (python.Text.Trim() == suggestedPython) python.Text = RememberedPython(current);
            suggestedPython = RememberedPython(current);
            previousHost = current;
        }));
        var browse = new Button { Content = "Browse Server…" };
        dialog.Add("", browse);
        browse.Click += async (_, _) =>
        {
            string target = host.Text.Trim();
            if (!ValidHost(target)) { MessageBox.Show(dialog, "Enter a valid SSH host or alias."); return; }
            browse.IsEnabled = false;
            try
            {
                if (await ChooseRemotePath(dialog, target, path.Text.Trim(), folders: true) is { } selected)
                    path.Text = selected;
            }
            catch (Exception ex) { if (dialog.IsVisible) MessageBox.Show(dialog, ex.Message, "Could not list server directory"); }
            finally { browse.IsEnabled = true; }
        };
        if (dialog.Confirm("Connect", () => !ValidHost(host.Text.Trim()) ? "Enter a valid SSH host or alias." :
            string.IsNullOrWhiteSpace(path.Text) || string.IsNullOrWhiteSpace(python.Text) ? "Enter a project directory and Python." : null))
            await OpenRemote(host.Text.Trim(), path.Text.Trim(), python.Text.Trim());
    }

    private async Task OpenRemote(string host, string path, string python, string? preferred = null)
    {
        if (busy || !ConfirmDiscardDrafts()) return;
        RemotePackageNeeds? needs = null;
        Task Connect() => During("Connecting over SSH…", async () =>
        {
            if (!ValidHost(host)) throw new ArgumentException("Invalid SSH host.");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host + "\n" + path)))[..24].ToLowerInvariant();
            string mirror = Path.Combine(settingsDirectory, "remote", hash);
            Directory.CreateDirectory(mirror);
            ResetProject();
            var remote = new RemoteProject(host, path, python, mirror);
            var client = SwitchEngine(ProjectPython(null), remote);
            try { remoteOpen = await client.CallAsync<RemoteOpenResult>("open", new { project = mirror }); }
            catch { needs = client.MissingRemotePackages; throw; }
            project = mirror;
            UpdateShellHeader();
            Title = $"Fignimble — {host}:{path}";
            if (TerminalRequested) StartShell();
            await RefreshProject(preferred);
            RememberProject(shownFigure);
        });
        await Connect();
        if (needs?.Pip is not { Length: > 0 } packages) return;
        var dialog = new FormDialog(this, "SSH Python Setup");
        dialog.Note($"{host}: {python}\n\nMissing engine packages: {string.Join(", ", packages)}.");
        dialog.Note("Install into this server environment only when you have permission. Cancel to choose a different Python.");
        if (!dialog.Confirm("Install and Reconnect", () => null)) return;
        bool installed = false;
        await During("Installing packages on the SSH server…", async () =>
        {
            using var helper = new EngineClient(ProjectPython(null));
            await helper.CallAsync<PackageInstall>("install_remote_engine_packages", new { host, python, packages });
            installed = true;
        });
        if (installed) await Connect();
    }

    private async Task ChooseRemotePanel()
    {
        if (!validFigure || shownFigure is not { } figure) return;
        var aliases = await RemoteHosts();
        var picker = new FormDialog(this, "Find a remote panel");
        var host = SshHostField(picker, aliases, engine?.Remote?.Host ?? aliases.FirstOrDefault() ?? "");
        var directory = picker.Text("Starting directory", "~");
        if (!picker.Confirm("Browse", () => ValidHost(host.Text.Trim()) ? null : "Enter a valid SSH host or alias.")) return;
        string target = host.Text.Trim();
        try
        {
            if (await ChooseRemotePath(this, target, directory.Text.Trim(), folders: false) is { } path && shownFigure == figure)
                await ImportRemotePanel(target, path);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private string RememberedPython(string host) => recentProjects.FirstOrDefault(r => r.Host == host)?.Python ?? "python3";

    private static ComboBox SshHostField(FormDialog dialog, string[] aliases, string preferred)
    {
        var host = new ComboBox { IsEditable = true, ItemsSource = aliases, Text = preferred, IsTextSearchEnabled = true };
        dialog.Add("Host alias or user@host", host);
        return host;
    }

    private async Task<SshListing> ListRemotePath(string host, string path)
    {
        using var temporary = engine is { Running: true } ? null : new EngineClient(ProjectPython(null));
        return await (temporary ?? engine!).CallAsync<SshListing>("remote_list", new { host, path });
    }

    private async Task<string?> ChooseRemotePath(Window owner, string host, string path, bool folders)
    {
        SshListing? listing = null;
        await During("Listing server directory…", async () => { listing = await ListRemotePath(host, path); });
        if (listing is null || !owner.IsVisible) return null;
        var dialog = new FormDialog(owner, folders ? "Browse for an SSH project" : "Browse for a remote Python panel") { Width = 520 };
        dialog.Note(folders ? "Open a folder to navigate. Use This Folder selects the current server directory."
            : "The selected script will run in the figure’s Python environment. Data dependencies are not copied.");
        var location = dialog.Text("Server directory", listing.Path);
        var entries = new ListBox { Height = 245 };
        dialog.Add("Folders and Python files", entries);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var go = new Button { Content = "Go to Directory", Margin = new Thickness(0, 0, 6, 0) };
        var up = new Button { Content = "Parent", Margin = new Thickness(0, 0, 6, 0) };
        var open = new Button { Content = "Open Folder" };
        buttons.Children.Add(go); buttons.Children.Add(up); buttons.Children.Add(open);
        dialog.Add("", buttons);
        void UpdateListing(SshListing found)
        {
            listing = found;
            location.Text = found.Path;
            entries.ItemsSource = folders ? found.Entries.Where(e => e.Dir).ToArray() : found.Entries;
        }
        bool loading = false;
        string Join(string parent, string name) => parent.TrimEnd('/') + "/" + name;
        async Task Go(string next)
        {
            if (loading) return;
            loading = true;
            entries.IsEnabled = buttons.IsEnabled = location.IsEnabled = false;
            try { UpdateListing(await ListRemotePath(host, next)); }
            catch (Exception ex) { if (dialog.IsVisible) MessageBox.Show(dialog, ex.Message, "Could not list server directory"); }
            finally { loading = false; entries.IsEnabled = buttons.IsEnabled = location.IsEnabled = true; }
        }
        UpdateListing(listing);
        go.Click += async (_, _) => await Go(location.Text.Trim());
        up.Click += async (_, _) => await Go(Join(listing.Path, ".."));
        open.Click += async (_, _) => { if (entries.SelectedItem is SshEntry { Dir: true } e) await Go(Join(listing.Path, e.Name)); };
        entries.MouseDoubleClick += async (_, _) => { if (entries.SelectedItem is SshEntry { Dir: true } e) await Go(Join(listing.Path, e.Name)); };
        if (!dialog.Confirm(folders ? "Use This Folder" : "Import Script", () => loading ? "Wait for the directory to load." :
            !folders && entries.SelectedItem is not SshEntry { Dir: false } ? "Select a Python script." : null)) return null;
        return folders ? listing.Path : Join(listing.Path, ((SshEntry)entries.SelectedItem).Name);
    }

    private bool CanRefreshRemotePanel
    {
        get
        {
            if (!validFigure || Page.Selection is not { Kind: "panel", Code.File: { } file }) return false;
            try { using var reader = File.OpenText(file); return reader.ReadLine()?.StartsWith("# Fetched from ", StringComparison.Ordinal) == true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    private Task ImportRemotePanel(string host, string path) => AddPanel("import_remote", "Importing remote panel…", (figure, box) =>
        new { figure, host, path, x = box.X, y = box.Y });

    private Task RefreshRemotePanel()
    {
        if (!CanRefreshRemotePanel || shownFigure is not { } figure || Page.Selection?.Code?.File is not { } file)
            return Task.CompletedTask;
        return During("Fetching updated panel…", async () =>
        {
            var result = await CallForCurrentFigure<EditResult>("update_remote", new { figure, file });
            ShowRender(result.Figure);
            await ReadState(figure);
        });
    }
}
