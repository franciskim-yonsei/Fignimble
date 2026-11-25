using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Fignimble.Windows;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private EngineClient? engine;
    private ProjectState? state;
    private string? project;
    private string? shownFigure;
    private bool validFigure;
    private bool busy;

    public MainWindow(string? initialPath)
    {
        InitializeComponent();
        reloadTimer.Tick += async (_, _) =>
        {
            if (busy || Page.IsDragging) return;
            reloadTimer.Stop();
            await Reload();
        };
        Page.EditCompleted += async edit => await CanvasEdited(edit);
        UpdateControls();
        Status.Text = "Open a local project. Drag a panel to move it, or drag its blue handle to resize it.";
        if (initialPath is not null) Loaded += async (_, _) => await OpenProject(initialPath);
    }

    protected override void OnClosed(EventArgs e)
    {
        reloadTimer.Stop();
        engine?.Dispose();
        base.OnClosed(e);
    }

    private EngineClient GetEngine()
    {
        if (engine is { Running: true }) return engine;
        engine?.Dispose();
        engine = null;
        engine = new EngineClient();
        engine.Notification += kind =>
        {
            if (kind != "changed") return;
            Dispatcher.BeginInvoke(() => { reloadTimer.Stop(); reloadTimer.Start(); });
        };
        return engine;
    }

    private async Task During(string status, Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UpdateControls();
        Status.Text = status;
        try { await action(); }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { busy = false; UpdateControls(); }
    }

    private void UpdateControls()
    {
        OpenButton.IsEnabled = !busy;
        Figures.IsEnabled = !busy && Figures.HasItems;
        ReloadButton.IsEnabled = !busy && project is not null;
        UndoButton.IsEnabled = !busy && state?.CanUndo == true;
        RedoButton.IsEnabled = !busy && state?.CanRedo == true;
        ExportButton.IsEnabled = !busy && validFigure;
        Page.IsEnabled = !busy && validFigure;
    }

    private async void OpenClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Open Fignimble project" };
        if (project is not null) dialog.InitialDirectory = project;
        if (dialog.ShowDialog(this) == true) await OpenProject(dialog.FolderName);
    }

    private Task OpenProject(string path) => During("Opening project…", async () =>
    {
        string fullPath = Path.GetFullPath(path);
        string folder = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath)! : fullPath;
        string? first = File.Exists(fullPath) ? Path.GetFileNameWithoutExtension(fullPath) : null;
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Project folder not found: {folder}");
        reloadTimer.Stop();
        state = null;
        project = null;
        Figures.ItemsSource = null;
        ClearFigure();
        Title = "Fignimble for Windows";
        await GetEngine().CallAsync<JsonElement>("open", new { project = folder });
        project = folder;
        Title = $"Fignimble — {Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}";
        await RefreshProject(first);
    });

    private Task Reload() => project is null ? Task.CompletedTask :
        During("Refreshing…", () => RefreshProject(Figures.SelectedItem as string));

    private async void ReloadClicked(object sender, RoutedEventArgs e) => await Reload();

    private async Task RefreshProject(string? preferred)
    {
        await ReadState(preferred);
        if (Figures.SelectedItem is string name) await RenderFigure(name);
        else
        {
            ClearFigure();
            Status.Text = "No Fignimble figures found in this folder.";
        }
    }

    private async Task ReadState(string? preferred)
    {
        state = await engine!.CallAsync<ProjectState>("state");
        string[] names = state.Figures.Select(f => f.Name).ToArray();
        if (!names.SequenceEqual(Figures.Items.Cast<string>())) Figures.ItemsSource = names;
        Figures.SelectedItem = names.Contains(preferred) ? preferred : names.FirstOrDefault();
    }

    private async void FigureChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!busy && Figures.SelectedItem is string name)
            await During($"Rendering {name}…", () => RenderFigure(name));
    }

    private async Task RenderFigure(string name)
    {
        if (name != shownFigure) ClearFigure();
        ShowRender(await engine!.CallAsync<FigureRender>("figure", new { name }));
    }

    private void ClearFigure()
    {
        shownFigure = null;
        validFigure = false;
        Page.ShowFigure(null, false);
        PageView.Visibility = Visibility.Collapsed;
        EmptyCanvas.Visibility = Visibility.Visible;
        EmptyCanvas.Text = project is null ? "Open a folder containing a Fignimble figure (.py)." : "No figure selected.";
        SourceTitle.Text = "Figure source";
        SourceText.Clear();
    }

    private void ShowRender(FigureRender render)
    {
        bool preserve = shownFigure == render.Figure;
        shownFigure = render.Figure;
        validFigure = render.Error is null && render.Size is [> 0, > 0];
        Page.ShowFigure(validFigure ? render : null, preserve);
        PageView.Visibility = validFigure ? Visibility.Visible : Visibility.Collapsed;
        EmptyCanvas.Visibility = validFigure ? Visibility.Collapsed : Visibility.Visible;
        EmptyCanvas.Text = validFigure ? "" : "The figure could not be rendered. See the status below.";
        double scroll = preserve ? SourceText.VerticalOffset : 0;
        SourceTitle.Text = Path.GetFileName(render.File) ?? "Figure source";
        SourceText.Text = File.Exists(render.File) ? File.ReadAllText(render.File) : "";
        SourceText.ScrollToVerticalOffset(scroll);
        Status.Text = render.Error?.Split('\n', 2)[0] ?? $"{render.Figure} — {render.Size?[0]:g} × {render.Size?[1]:g} mm";
    }

    private Task CanvasEdited(CanvasEdit edit)
    {
        if (!validFigure || shownFigure is not { } name) return Task.CompletedTask;
        object args = edit.NewBox is { } box
            ? new { figure = name, op = "resize", oid = edit.Oid, box = new[] { box.X, box.Y, box.Width, box.Height } }
            : new { figure = name, op = "move", oids = new[] { edit.Oid }, dx = edit.Delta.X, dy = edit.Delta.Y };
        return During("Updating figure…", async () =>
        {
            var result = await engine!.CallAsync<EditResult>("edit", args);
            ShowRender(result.Figure);
            await ReadState(name);
        });
    }

    private Task History(string command) => shownFigure is not { } figure ? Task.CompletedTask :
        During(command == "undo" ? "Undoing…" : "Redoing…", async () =>
        {
            try
            {
                var result = await engine!.CallAsync<EditResult>(command, new { figure });
                ShowRender(result.Figure);
            }
            finally { await ReadState(figure); }
        });

    private async void UndoClicked(object sender, RoutedEventArgs e) => await History("undo");
    private async void RedoClicked(object sender, RoutedEventArgs e) => await History("redo");

    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (!validFigure || shownFigure is not { } figure) return;
        var dialog = new SaveFileDialog { Title = "Export figure", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = ".pdf",
            FileName = figure + ".pdf", InitialDirectory = project };
        if (dialog.ShowDialog(this) != true) return;
        await ExportPdf(figure, dialog.FileName);
    }

    private Task ExportPdf(string figure, string path) => During("Exporting PDF…", async () =>
    {
        await engine!.CallAsync<JsonElement>("export", new { figure, path, format = "pdf" });
        Status.Text = $"Exported {path}";
    });

    private async void HandleKey(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control && e.Key == Key.Z && UndoButton.IsEnabled)
        {
            e.Handled = true;
            await History("undo");
        }
        else if (((modifiers == ModifierKeys.Control && e.Key == Key.Y) ||
                  (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)) && RedoButton.IsEnabled)
        {
            e.Handled = true;
            await History("redo");
        }
    }
}
