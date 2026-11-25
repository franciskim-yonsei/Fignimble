using System.Windows;

namespace Fignimble.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow(e.Args.FirstOrDefault()).Show();
    }
}
