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
    private FigureRender? currentRender;
    private bool validFigure;
    private bool busy;

    public MainWindow(string? initialPath)
    {
        InitializeComponent();
        reloadTimer.Tick += async (_, _) =>
        {
            if (busy || editingSource || Page.IsDragging) return;
            reloadTimer.Stop();
            await Reload();
        };
        Page.EditCompleted += async edit => await CanvasEdited(edit);
        Page.DividerEdited += async (oid, index, delta) => await MoveDivider(oid, index, delta);
        Page.PlotTextClicked += async (layer, text) => await EditClickedPlotText(layer, text);
        Page.SelectionChanged += CanvasSelectionChanged;
        UpdateControls();
        SyncGeometry();
        Status.Text = "Open a project, or create one and try Add sample plot.";
        if (initialPath is not null) Loaded += async (_, _) => await OpenProject(initialPath);
    }

    protected override void OnClosed(EventArgs e)
    {
        reloadTimer.Stop();
        terminal?.Dispose();
        engine?.Dispose();
        base.OnClosed(e);
    }

    private EngineClient GetEngine(string? targetProject = null)
    {
        string python = EngineClient.ResolvePython(targetProject ?? project);
        if (engine is { Running: true } && string.Equals(engine.Python, python, StringComparison.OrdinalIgnoreCase)) return engine;
        engine?.Dispose();
        engine = null;
        var client = new EngineClient(python);
        client.Notification += kind =>
        {
            if (kind != "changed") return;
            Dispatcher.BeginInvoke(() =>
            {
                if (engine != client) return;
                reloadTimer.Stop();
                reloadTimer.Start();
            });
        };
        return engine = client;
    }

    private async Task During(string status, Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UpdateControls();
        Status.Text = status;
        try { await action(); }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally
        {
            busy = false;
            if (reloadTimer.IsEnabled) { reloadTimer.Stop(); reloadTimer.Start(); }
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        NewProjectButton.IsEnabled = OpenButton.IsEnabled = !busy;
        Figures.IsEnabled = !busy && Figures.HasItems;
        NewFigureButton.IsEnabled = ReloadButton.IsEnabled = !busy && project is not null;
        ImportButton.IsEnabled = SampleButton.IsEnabled = !busy && validFigure;
        Geometry.IsEnabled = !busy && validFigure;
        AnnotationButton.IsEnabled = !busy && validFigure;
        UpdateLayerControls();
        UndoButton.IsEnabled = !busy && state?.CanUndo == true;
        RedoButton.IsEnabled = !busy && state?.CanRedo == true;
        ExportButton.IsEnabled = !busy && validFigure;
        Page.IsEnabled = !busy && validFigure;
        MoreButton.IsEnabled = !busy && project is not null;
        TerminalButton.IsEnabled = !busy && project is not null;
        EditSourceButton.IsEnabled = !busy && currentRender is not null;
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
        bool file = File.Exists(fullPath);
        await OpenProjectCore(file ? Path.GetDirectoryName(fullPath)! : fullPath,
            file ? Path.GetFileNameWithoutExtension(fullPath) : null);
    });

    private async Task OpenProjectCore(string folder, string? first)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Project folder not found: {folder}");
        reloadTimer.Stop();
        terminal?.Stop();
        terminalFolder = null;
        state = null;
        project = null;
        Figures.ItemsSource = null;
        ClearFigure();
        Title = "Fignimble for Windows";
        await GetEngine(folder).CallAsync<JsonElement>("open", new { project = folder });
        project = folder;
        if (TerminalDrawer.Visibility == Visibility.Visible) StartShell();
        Title = $"Fignimble — {Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}";
        await RefreshProject(first);
    }

    private Task Reload() => project is null ? Task.CompletedTask :
        During("Refreshing…", () => RefreshProject(Figures.SelectedItem as string, external: true));

    private async void ReloadClicked(object sender, RoutedEventArgs e) => await Reload();

    private async Task RefreshProject(string? preferred, bool external = false)
    {
        await ReadState(preferred);
        if (Figures.SelectedItem is string name) await RenderFigure(name, external);
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

    private async Task RenderFigure(string name, bool external = false)
    {
        if (name != shownFigure) ClearFigure();
        ShowRender(await engine!.CallAsync<FigureRender>("figure", new { name }), external);
    }

    private void ClearFigure()
    {
        shownFigure = null;
        currentRender = null;
        validFigure = false;
        Page.ShowFigure(null, false);
        RefreshLayers();
        PageView.Visibility = Visibility.Collapsed;
        ScheduleRulers();
        EmptyCanvas.Visibility = Visibility.Visible;
        EmptyCanvas.Text = project is null ? "Open a folder containing a Fignimble figure (.py)." : "No figure selected.";
        SourceTitle.Text = "Figure source";
        SourceText.Clear();
        SyncGeometry();
        ShowWarnings(null);
    }

    private void ShowRender(FigureRender render, bool external = false)
    {
        string source = File.Exists(render.File) ? File.ReadAllText(render.File) : "";
        // A source change can reorder the OIDs. Asset-only changes and duplicate notifications can keep the selection.
        bool preserve = shownFigure == render.Figure && (!external || source == SourceText.Text);
        shownFigure = render.Figure;
        currentRender = render;
        validFigure = render.Error is null && render.Size is [> 0, > 0];
        Page.ShowFigure(validFigure ? render : null, preserve);
        RefreshLayers();
        UpdateZoom();
        PageView.Visibility = validFigure ? Visibility.Visible : Visibility.Collapsed;
        ScheduleRulers();
        EmptyCanvas.Visibility = validFigure ? Visibility.Collapsed : Visibility.Visible;
        EmptyCanvas.Text = validFigure ? "" : "The figure could not be rendered. See the status below.";
        double scroll = preserve ? SourceText.VerticalOffset : 0;
        SourceTitle.Text = Path.GetFileName(render.File) ?? "Figure source";
        SourceText.Text = source;
        SourceText.ScrollToVerticalOffset(scroll);
        SyncGeometry();
        ShowWarnings(render.Warnings);
        bool empty = render.Elements is not { Length: > 0 };
        Status.Text = render.Error?.Split('\n', 2)[0] ?? $"{render.Figure} — {render.Size?[0]:g} × {render.Size?[1]:g} mm" +
            (empty ? " · Import a panel or add a sample plot to get started." : "");
    }

    private Task CanvasEdited(CanvasEdit edit)
    {
        if (!validFigure || shownFigure is not { } name) return Task.CompletedTask;
        object args = edit.NewBox is { } box
            ? currentRender?.Elements?.FirstOrDefault(e => e.Oid == edit.Oids[0])?.Kind == "layout"
                ? new { figure = name, op = "set", oid = edit.Oids[0], props = new { x = box.X, y = box.Y, w = box.Width, h = box.Height } }
                : new { figure = name, op = "resize", oid = edit.Oids[0], box = new[] { box.X, box.Y, box.Width, box.Height } }
            : new { figure = name, op = "move", oids = edit.Oids, dx = edit.Delta.X, dy = edit.Delta.Y };
        return EditFigure(name, args, "Updating figure…");
    }

    private Task EditFigure(string figure, object args, string status, bool clearSelection = false) => During(status, async () =>
    {
        var result = await engine!.CallAsync<EditResult>("edit", args);
        if (clearSelection && !result.Unchanged) Page.Select(null);
        ShowRender(result.Figure);
        if (result.Select is { Length: > 0 }) Page.SelectIds(result.Select);
        await ReadState(figure);
    });

    private Task History(string command) => project is null ? Task.CompletedTask :
        During(command == "undo" ? "Undoing…" : "Redoing…", async () =>
        {
            string? figure = shownFigure;
            try { await engine!.CallAsync<JsonElement>(command); }
            catch { await ReadState(figure); throw; }
            Page.Select(null);
            await RefreshProject(figure);
        });

    private async void UndoClicked(object sender, RoutedEventArgs e) => await History("undo");
    private async void RedoClicked(object sender, RoutedEventArgs e) => await History("redo");

    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (!validFigure || shownFigure is not { } figure) return;
        await ChooseExport(figure);
    }

    private Task ExportFigure(string figure, string path, int dpi, string journal) => During("Exporting figure…", async () =>
    {
        string format = Path.GetExtension(path).ToLowerInvariant();
        if (format is not (".pdf" or ".svg" or ".png" or ".tiff" or ".pptx"))
            throw new ArgumentException("Choose a .pdf, .svg, .png, .tiff, or .pptx filename.");
        var result = await engine!.CallAsync<ExportResult>("export", new { figure, path, format = format[1..], dpi,
            journal = string.IsNullOrEmpty(journal) ? null : journal });
        if (result.Warnings is { Length: > 0 } warnings)
        {
            Warnings.ItemsSource = warnings.Select(w => "Export: " + w).Concat(Warnings.Items.Cast<string>()).Distinct().ToArray();
            Checks.IsExpanded = true;
            Status.Text = $"Exported {path} · {warnings.Length} warning(s); see Preflight.";
        }
        else Status.Text = $"Exported {path}";
    });

    private async void HandleKey(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (!busy && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase &&
            FigureShortcut(e.Key, modifiers) is { } shortcut) { e.Handled = true; await shortcut; return; }
        if (e.Key == Key.Escape && !busy && !Page.IsDragging && Page.SelectedIds.Count > 0)
        {
            e.Handled = true;
            Page.Select(null);
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.Z && UndoButton.IsEnabled)
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
