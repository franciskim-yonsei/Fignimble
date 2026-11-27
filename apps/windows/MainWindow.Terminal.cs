using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private sealed class ShellTab(int number)
    {
        public int Number { get; } = number;
        public TerminalPanel First { get; set; } = new();
        public TerminalPanel? Second { get; set; }
        public TerminalPanel? Active { get; set; }
        public Grid? Split { get; set; }
    }

    private readonly List<ShellTab> shells = [];
    private ShellTab? shellTab;
    private int nextShell = 1;
    private bool updatingShellTabs;
    private TerminalPanel? terminal => shellTab?.Active ?? shellTab?.First;
    private bool TerminalRequested => DrawerTabs.Visibility == Visibility.Visible && DrawerTabs.SelectedItem == TerminalTab;
    private void TerminalClicked(object sender, RoutedEventArgs e) => ToggleTerminal();

    private void ToggleTerminal()
    {
        if (project is null || engine is null) return;
        ShowDrawer(TerminalRequested ? CodeTab : TerminalTab);
    }

    private void RestartShellClicked(object sender, RoutedEventArgs e) => StartShell();
    private void NewShellClicked(object sender, RoutedEventArgs e) => OpenNewShell();

    private string AgentCommandFile => System.IO.Path.Combine(settingsDirectory, "terminal-command.json");

    private string TerminalCommand()
    {
        try
        {
            if (System.IO.File.Exists(AgentCommandFile) &&
                System.Text.Json.JsonSerializer.Deserialize<string>(System.IO.File.ReadAllText(AgentCommandFile)) is { Length: > 0 } command &&
                !command.Any(char.IsControl)) return command;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        return "claude";
    }

    private void RunTerminalCommandClicked(object sender, RoutedEventArgs e)
    {
        if (terminal is not { IsRunning: true } shell) { Status.Text = "Start a terminal session first."; return; }
        var dialog = new FormDialog(this, "Run a terminal command");
        dialog.Note("Runs in the active terminal on this machine or the connected server. It does not install tools or accept prompts for you.");
        string[] commands = ["claude", "codex", "gemini", "opencode", "pi", "Custom"];
        string initial = TerminalCommand();
        var choice = dialog.Choice("Launcher", commands, commands.Contains(initial) ? initial : "Custom");
        var input = dialog.Text("Command to type", initial);
        choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is string next && next != "Custom") input.Text = next; };
        if (!dialog.Confirm("Type and run", () => input.Text.Trim() is not { Length: > 0 and <= 300 } cmd || cmd.Any(char.IsControl)
            ? "Enter a single command of up to 300 characters." : null)) return;
        string command = input.Text.Trim();
        try { SaveSetting(AgentCommandFile, command); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status.Text = ex.Message; }
        if (shell.IsRunning) { shell.Type(command + "\r"); shell.FocusTerminal(); }
    }

    private void OpenNewShell()
    {
        if (!CanStartProjectShell()) return;
        AddShellTab();
        ShowDrawer(TerminalTab);
    }

    private void AddShellTab()
    {
        var tab = new ShellTab(nextShell++);
        shells.Add(tab);
        TrackActive(tab, tab.First);
        ShowShell(tab);
        LaunchShell(tab.First);
    }

    private static void TrackActive(ShellTab tab, TerminalPanel panel)
    {
        panel.IsKeyboardFocusWithinChanged += (_, _) => { if (panel.IsKeyboardFocusWithin) tab.Active = panel; };
        panel.PreviewMouseDown += (_, _) => tab.Active = panel;
    }

    private void ShowShell(ShellTab? tab)
    {
        updatingShellTabs = true;
        try
        {
            ShellTabs.Items.Clear();
            foreach (var shell in shells)
            {
                var item = new ListBoxItem { Content = $"Shell {shell.Number}", Tag = shell, Padding = new Thickness(7, 2, 7, 2) };
                ShellTabs.Items.Add(item);
                if (shell == tab) ShellTabs.SelectedItem = item;
            }
        }
        finally { updatingShellTabs = false; }
        DisplayShell(tab);
    }

    private void DisplayShell(ShellTab? tab)
    {
        shellTab = tab;
        TerminalHost.Content = tab?.Split is { } split ? split : tab?.First;
        SplitShellButton.IsEnabled = tab is { Second: null };
        KillShellButton.IsEnabled = tab is not null;
    }

    private void ShellTabSelected(object sender, SelectionChangedEventArgs e)
    {
        if (updatingShellTabs || ShellTabs.SelectedItem is not ListBoxItem { Tag: ShellTab tab }) return;
        DisplayShell(tab);
        terminal?.FocusTerminal();
    }

    private void SplitShellClicked(object sender, RoutedEventArgs e)
    {
        if (shellTab is not { Second: null } tab || !CanStartProjectShell()) return;
        TerminalHost.Content = null;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 70 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 70 });
        var split = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(split, 1);
        var other = new TerminalPanel();
        System.Windows.Automation.AutomationProperties.SetName(other, "Split terminal");
        Grid.SetColumn(other, 2);
        grid.Children.Add(tab.First);
        grid.Children.Add(split);
        grid.Children.Add(other);
        tab.Second = tab.Active = other;
        tab.Split = grid;
        TrackActive(tab, other);
        DisplayShell(tab);
        LaunchShell(other);
    }

    private void KillShellClicked(object sender, RoutedEventArgs e)
    {
        if (shellTab is not { } tab) return;
        TerminalHost.Content = null;
        if (tab.Second is { } other)
        {
            var active = tab.Active ?? tab.First;
            var survivor = active == tab.First ? other : tab.First;
            tab.Split!.Children.Clear();
            active.Dispose();
            tab.First = tab.Active = survivor;
            tab.Second = null;
            tab.Split = null;
            Grid.SetColumn(survivor, 0);
            DisplayShell(tab);
        }
        else
        {
            int index = shells.IndexOf(tab);
            tab.First.Dispose();
            shells.RemoveAt(index);
            ShowShell(shells.Count == 0 ? null : shells[Math.Min(index, shells.Count - 1)]);
        }
        terminal?.FocusTerminal();
    }

    private void StopProjectShells()
    {
        TerminalHost.Content = null;
        foreach (var tab in shells) { tab.First.Dispose(); tab.Second?.Dispose(); }
        shells.Clear();
        nextShell = 1;
        ShowShell(null);
    }

    private bool ConfirmCloseShells() => !shells.Any(tab => tab.First.IsRunning || tab.Second?.IsRunning == true) ||
        MessageBox.Show(this, "Close the running terminal sessions? Commands still running in them may be interrupted.",
            "Close terminals", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private void UpdateShellHeader()
    {
        bool remote = project is not null && engine?.Remote is not null;
        MirrorShellButton.Visibility = remote ? Visibility.Visible : Visibility.Collapsed;
        string title = remote && engine?.Remote is { } target
            ? $"SSH: {target.Host}:{remoteOpen?.RemotePath ?? target.Path}"
            : "Project shell (PowerShell)";
        TerminalTitle.Text = title;
        TerminalTitle.ToolTip = title;
    }

    private bool CanStartProjectShell()
    {
        if (project is null || engine is null) return false;
        if (engine.Remote is { Interactive: true } remote && SignedIn(remote.Host) != engine.Ssh)
        {
            Status.Text = "Use SSH → Reconnect to sign in again before opening a shell.";
            return false;
        }
        if (engine.Remote is null || remoteOpen?.TerminalArgv is { Length: > 0 }) return true;
        Status.Text = "SSH shell information is missing. Reconnect to the SSH project and try again.";
        return false;
    }

    private void StartShell()
    {
        if (!CanStartProjectShell()) return;
        if (terminal is { } panel) LaunchShell(panel);
        else AddShellTab();
    }

    private void LaunchShell(TerminalPanel panel)
    {
        if (!CanStartProjectShell()) return;
        if (engine!.Remote is null) panel.Start(project!, engine.Python, engine.EngineRoot, System.IO.Path.Combine(settingsDirectory, "bin"));
        else if (engine.Ssh is { } ssh) panel.StartRemote(project!, ssh, remoteOpen!.TerminalArgv![^1]);
        else panel.StartRemote(project!, remoteOpen!.TerminalArgv!);
        panel.FocusTerminal();
    }

    private void OpenMirrorShellClicked(object sender, RoutedEventArgs e)
    {
        if (project is null || engine?.Remote is null) return;
        try
        {
            using var shell = Process.Start(new ProcessStartInfo(TerminalPanel.PowerShellExe, "-NoLogo")
                { WorkingDirectory = project, UseShellExecute = true });
        }
        catch (Exception ex) { Status.Text = $"Could not open the local mirror shell: {ex.Message}"; }
    }
}
