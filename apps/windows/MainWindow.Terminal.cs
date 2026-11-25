using System.Windows;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private TerminalPanel? terminal;
    private string? terminalFolder;

    private void TerminalClicked(object sender, RoutedEventArgs e) => ToggleTerminal();

    private void ToggleTerminal()
    {
        if (TerminalDrawer.Visibility == Visibility.Visible)
        {
            TerminalDrawer.Visibility = Visibility.Collapsed; // Keep the shell alive while hidden.
            return;
        }
        if (project is null || engine is null) return;
        TerminalDrawer.Visibility = Visibility.Visible;
        if (terminalFolder != project) StartShell();
        else terminal?.FocusTerminal();
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
