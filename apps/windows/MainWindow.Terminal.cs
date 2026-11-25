using System.Windows;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private TerminalPanel? terminal;
    private string? terminalFolder;

    private void TerminalClicked(object sender, RoutedEventArgs e) => ToggleTerminal();

    private bool TerminalRequested => DrawerTabs.Visibility == Visibility.Visible && DrawerTabs.SelectedItem == TerminalTab;

    private void ToggleTerminal()
    {
        if (project is null || engine is null) return;
        ShowDrawer(TerminalRequested ? CodeTab : TerminalTab);
    }

    private void RestartShellClicked(object sender, RoutedEventArgs e) => StartShell();

    private void StartShell()
    {
        if (project is not { } folder || engine is not { } client) return;
        if (terminal is null) TerminalHost.Content = terminal = new TerminalPanel();
        terminalFolder = folder;
        terminal.Start(folder, client.Python, client.EngineRoot);
        terminal.FocusTerminal();
    }
}
