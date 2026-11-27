using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Fignimble.Windows;
using ICSharpCode.AvalonEdit;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "fignimble-navigation-" + Guid.NewGuid().ToString("N"));
        if (args is ["--test-managed-python"]) return CheckManagedPython(root);
        string fixture = Directory.CreateDirectory(Path.Combine(root, "engine", "fignimble")).FullName;
        string project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "rpc.py"), Path.Combine(fixture, "rpc.py"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "requirements.py"), Path.Combine(fixture, "requirements.py"));
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

    private static int CheckManagedPython(string root)
    {
        var setup = typeof(MainWindow).Assembly.GetType("Fignimble.Windows.ManagedPython")!.GetMethod("SetUp")!;
        try
        {
            string python = ((Task<string>)setup.Invoke(null, [root, null])!).GetAwaiter().GetResult();
            var start = new ProcessStartInfo(python) { UseShellExecute = false };
            foreach (string part in new[] { "-c", "import sys, pip; assert sys.prefix != sys.base_prefix; print(sys.version)" })
                start.ArgumentList.Add(part);
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000) || process.ExitCode != 0)
                throw new Exception("The managed Python environment did not start with pip.");
            Console.WriteLine("Verified the Python download and private environment on Windows.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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

            string holdState = Path.Combine(project, "hold-state");
            File.WriteAllText(holdState, "");
            var reload = (Task)typeof(MainWindow).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            try
            {
                await Until(() => File.Exists(Path.Combine(project, "started-state")), () => status.Text);
                if (!figures.IsEnabled) throw new Exception("Automatic refresh disabled figure selection.");
                figures.SelectedIndex = 0;
            }
            finally { File.Delete(holdState); }
            await reload.WaitAsync(TimeSpan.FromSeconds(15));
            if (figures.SelectedIndex != 0 || !status.Text.StartsWith("first —"))
                throw new Exception("The automatic refresh discarded a selection made while reading project state.");

            var editor = (TextEditor)window.FindName("SourceText");
            const string draft = "# Draft to save before switching\n";
            editor.Text = draft;
            var click = window.Dispatcher.InvokeAsync(() =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Unsaved figure changes");
                var button = Descendants(dialog).OfType<Button>().Single(b => (string)b.Content == "Apply and Switch");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.ApplicationIdle);
            figures.SelectedIndex = 3;
            await click.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Until(() => page.IsEnabled && status.Text.StartsWith("last —"), () => status.Text);
            if (File.ReadAllText(Path.Combine(project, "first.py")) != draft)
                throw new Exception("The draft was not applied before switching figures.");
            Console.WriteLine("Verified navigation during rendering and automatic refresh, recovery from errors, and applying a draft while switching.");
        }
        finally
        {
            File.WriteAllText(unblock, "");
            statusProperty.RemoveValueChanged(status, recordStatus);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject element)
    {
        yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(element, i))) yield return child;
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
