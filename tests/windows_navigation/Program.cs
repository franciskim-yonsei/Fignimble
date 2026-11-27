using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Fignimble.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "fignimble-navigation-" + Guid.NewGuid().ToString("N"));
        string fixture = Directory.CreateDirectory(Path.Combine(root, "engine", "fignimble")).FullName;
        string project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "rpc.py"), Path.Combine(fixture, "rpc.py"));
        foreach (var name in new[] { "first", "slow", "skipped", "last", "failed" })
            File.WriteAllText(Path.Combine(project, name + ".py"), "# Navigation test fixture\n");
        Environment.SetEnvironmentVariable("FIGNIMBLE_PYTHON", args[0]);
        Environment.SetEnvironmentVariable("FIGNIMBLE_ENGINE", Path.GetDirectoryName(fixture));
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [project, Path.Combine(root, "settings")], null)!;
            app.Startup += async (_, _) =>
            {
                int exit = 0;
                try { await CheckNavigation(window, project); }
                catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
                finally { app.Shutdown(exit); }
            };
            return app.Run(window);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task CheckNavigation(MainWindow window, string project)
    {
        var figures = (ListBox)window.FindName("Figures");
        var status = (TextBlock)window.FindName("Status");
        var page = (FigureCanvas)window.FindName("Page");
        string requests = Path.Combine(project, "renders");
        string unblock = Path.Combine(project, "finish-slow");
        var shown = new List<string>();
        var statusProperty = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        EventHandler recordStatus = (_, _) => shown.Add(status.Text);
        statusProperty.AddValueChanged(status, recordStatus);
        try
        {
            await Until(() => figures.IsEnabled && status.Text.StartsWith("first —"), () => status.Text);
            figures.SelectedIndex = 1;
            await Until(() => File.Exists(Path.Combine(project, "started-slow")), () => status.Text);
            if (!figures.IsEnabled || page.IsEnabled)
                throw new Exception("Navigation must stay enabled while the previous figure is protected from edits.");

            figures.SelectedIndex = 2;
            figures.SelectedIndex = 3;
            if (status.Text != "Opening last…") throw new Exception($"The last selection was ignored: {status.Text}");
            File.WriteAllText(unblock, "");
            await Until(() => page.IsEnabled && status.Text.StartsWith("last —"), () => status.Text);
            if (!File.ReadAllLines(requests).SequenceEqual(new[] { "first", "slow", "last" }) ||
                shown.Any(text => text.StartsWith("slow —") || text.StartsWith("skipped —")))
                throw new Exception("An obsolete figure was requested or shown.");

            figures.SelectedIndex = 4;
            await Until(() => page.IsEnabled && status.Text == "Figure not found", () => status.Text);
            if (figures.SelectedIndex != 3 || !figures.IsEnabled)
                throw new Exception("A failed selection did not return to the last figure.");
            Console.WriteLine("Verified that the Windows figure list stays enabled, shows the latest selection, and recovers from errors.");
        }
        finally
        {
            File.WriteAllText(unblock, "");
            statusProperty.RemoveValueChanged(status, recordStatus);
        }
    }

    private static async Task Until(Func<bool> ready, Func<string> status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (!ready()) await Task.Delay(25, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"The figure list did not reach the expected state. Status was: {status()}");
        }
    }
}
