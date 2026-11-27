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
    private bool navigatingFigures, restoringFigure;

    public MainWindow(string? initialPath) : this(initialPath,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fignimble")) { }

    internal MainWindow(string? initialPath, string settingsDirectory)
    {
        this.settingsDirectory = settingsDirectory;
        InitializeComponent();
        SetupWorkspace();
        SetupFigureFiles();
        SetupArrangement();
        SetupCode();
        SetupDrawing();
        SetupViewPreferences();
        RestoreWindowFrame();
        LoadPythonSettings();
        SetupMenus();
        reloadTimer.Tick += async (_, _) =>
        {
            if (busy || Page.IsDragging) return;
            reloadTimer.Stop();
            await Reload();
        };
        Page.EditCompleted += async edit => await CanvasEdited(edit);
        Page.DividerEdited += async (oid, index, delta) => await MoveDivider(oid, index, delta);
        Page.GuideEdited += async (index, value) => await MoveCanvasGuide(index, value);
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
        thumbnailTimer.Stop();
        SaveWindowFrame();
        SaveViewPreferences();
        StopProjectShells();
        var client = engine;
        engine = null;
        client?.Dispose();
        foreach (var session in sshSessions.Values) session.Dispose();
        base.OnClosed(e);
    }

    private EngineClient GetEngine(string? targetProject = null)
    {
        string python = ProjectPython(targetProject ?? project);
        if (engine is { Running: true, Remote: null } && string.Equals(engine.Python, python, StringComparison.OrdinalIgnoreCase)) return engine;
        return SwitchEngine(python, null);
    }

    private EngineClient SwitchEngine(string python, RemoteProject? remote, InteractiveSshSession? ssh = null)
    {
        engine?.Dispose();
        engine = null;
        Page.ClearSpriteCache();
        var client = new EngineClient(python, remote, ssh);
        client.Notification += kind =>
        {
            if (kind is not ("changed" or "rendered" or "disconnected")) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (engine != client) return;
                if (kind == "disconnected")
                {
                    reloadTimer.Stop();
                    thumbnailTimer.Stop();
                    if (project is not null && client.Remote is { } target)
                        Status.Text = $"Disconnected from {target.Host}. Use SSH → Reconnect to reopen this project.";
                    return;
                }
                if (kind == "rendered") { thumbnailTimer.Start(); return; }
                reloadTimer.Stop();
                reloadTimer.Start();
            });
        };
        return engine = client;
    }

    private async Task During(string status, Func<Task> action, bool figureNavigation = false)
    {
        if (busy) return;
        busy = true;
        navigatingFigures = figureNavigation;
        UpdateControls();
        Status.Text = status;
        try { await action(); }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally
        {
            busy = navigatingFigures = false;
            if (reloadTimer.IsEnabled) { reloadTimer.Stop(); reloadTimer.Start(); }
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        NewProjectButton.IsEnabled = OpenButton.IsEnabled = RemoteButton.IsEnabled = !busy;
        Figures.IsEnabled = (!busy || navigatingFigures) && Figures.HasItems;
        NewFigureButton.IsEnabled = ReloadButton.IsEnabled = !busy && project is not null;
        ImportButton.IsEnabled = SampleButton.IsEnabled = !busy && validFigure;
        Geometry.IsEnabled = PropertyEditor.IsEnabled = !busy && validFigure;
        AnnotationButton.IsEnabled = CanvasDrawingTools.IsEnabled = !busy && validFigure;
        UpdateLayerControls();
        UndoButton.IsEnabled = !busy && state?.CanUndo == true;
        RedoButton.IsEnabled = !busy && state?.CanRedo == true;
        ExportButton.IsEnabled = !busy && validFigure;
        Page.IsEnabled = !busy && validFigure;
        MoreButton.IsEnabled = !busy && project is not null;
        RecentButton.Visibility = project is null ? Visibility.Visible : Visibility.Collapsed;
        RecentButton.IsEnabled = !busy;
        PythonSetupButton.Visibility = project is null ? Visibility.Visible : Visibility.Collapsed;
        PythonSetupButton.IsEnabled = !busy && engine?.Remote is null;
        TerminalButton.IsEnabled = TerminalTab.IsEnabled = !busy && project is not null;
        InspectorFirst.IsEnabled = InspectorSecond.IsEnabled = !busy && validFigure;
        UpdateCodeControls();
        UpdateReviewControls();
    }

    private async void OpenClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Open Fignimble project" };
        if (project is not null) dialog.InitialDirectory = project;
        if (dialog.ShowDialog(this) == true) await OpenProject(dialog.FolderName);
    }

    private Task OpenProject(string path) => During("Opening project…", async () =>
    {
        if (!ConfirmDiscardDrafts()) return;
        string fullPath = Path.GetFullPath(path);
        bool file = File.Exists(fullPath);
        await OpenProjectCore(file ? Path.GetDirectoryName(fullPath)! : fullPath,
            file ? Path.GetFileNameWithoutExtension(fullPath) : null);
    });

    private async Task OpenProjectCore(string folder, string? first)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Project folder not found: {folder}");
        var packages = await PrepareLocalPython(folder);
        if (packages is null) return;
        await OfferProjectPackages(packages, folder);
        ResetProject();
        await GetEngine(folder).CallAsync<JsonElement>("open", new { project = folder });
        project = folder;
        UpdateShellHeader();
        if (TerminalRequested) StartShell();
        Title = $"Fignimble — {Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}";
        await RefreshProject(first);
        RememberProject(shownFigure);
    }

    private void ResetProject()
    {
        reloadTimer.Stop();
        StopProjectShells();
        remoteOpen = null;
        state = null;
        project = null;
        UpdateShellHeader();
        zoom = 1;
        Viewport.ScrollToHome();
        Figures.ItemsSource = null;
        figureCards.Clear();
        RefreshFigureEmpty();
        Page.ClearSpriteCache();
        ClearFigure();
        Title = "Fignimble for Windows";
    }

    private Task Reload() => project is null || engine is not { Running: true } client ? Task.CompletedTask :
        During("Refreshing…", async () =>
        {
            var updated = await client.CallAsync<ProjectState>("state");
            if (client != engine) return;
            string? preferred = (Figures.SelectedItem as FigureCard)?.Name ?? shownFigure;
            ApplyProjectState(updated, preferred);
            if (preferred == shownFigure && (CodeDirty || CaptionDirty) && (Figures.SelectedItem as FigureCard)?.Name != shownFigure)
            {
                RestoreFigureSelection();
                Status.Text = "The current figure is no longer available. Your draft is still in the drawer.";
                return;
            }
            if (Figures.SelectedItem is null)
            {
                ClearFigure();
                Status.Text = "No Fignimble figures found in this folder.";
                return;
            }
            await RenderSelectedFigure(external: true);
        }, figureNavigation: true);

    private Task RenderAgain() => project is null ? Task.CompletedTask : During("Rendering again…", async () =>
    {
        if (shownFigure is { } figure) await RenderFigure(figure, external: true, force: true);
        await ReadState(shownFigure);
    });

    private async void ReloadClicked(object sender, RoutedEventArgs e) => await RenderAgain();

    private async Task RefreshProject(string? preferred, bool external = false, ProjectState? initialState = null)
    {
        await ReadState(preferred, initialState);
        if (external && (CodeDirty || CaptionDirty) && (Figures.SelectedItem as FigureCard)?.Name != shownFigure)
        {
            Status.Text = "The current figure is no longer available. Your draft is still in the drawer.";
            return;
        }
        if (Figures.SelectedItem is FigureCard card) await RenderFigure(card.Name, external);
        else
        {
            ClearFigure();
            Status.Text = "No Fignimble figures found in this folder.";
        }
    }

    private async Task ReadState(string? preferred, ProjectState? initialState = null)
        => ApplyProjectState(initialState ?? await engine!.CallAsync<ProjectState>("state"), preferred);

    private void ApplyProjectState(ProjectState updated, string? preferred)
    {
        state = updated;
        restoringFigure = true;
        try { SetFigureCards(updated.Figures, preferred); }
        finally { restoringFigure = false; }
        RefreshCodeFiles();
        RefreshHistory();
    }

    private async void FigureChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Figures || restoringFigure || Figures.SelectedItem is not FigureCard card) return;
        if (navigatingFigures)
        {
            Status.Text = $"Opening {card.Name}…";
            return;
        }
        if (busy || engine is null || card.Name == shownFigure) return;
        await During($"Opening {card.Name}…", () => RenderSelectedFigure(), figureNavigation: true);
    }

    private async Task RenderSelectedFigure(bool external = false)
    {
        // One render at a time, including file-watcher refreshes. An intervening click takes precedence.
        bool draftsResolved = false;
        while (engine is not null && Figures.SelectedItem is FigureCard selected)
        {
            if (!draftsResolved && selected.Name != shownFigure)
            {
                if (!ConfirmFigureAction("Switch"))
                {
                    RestoreFigureSelection();
                    Status.Text = shownFigure is null ? "Figure switch canceled." : $"Still showing {shownFigure}.";
                    break;
                }
                draftsResolved = true;
            }
            await RenderFigure(selected.Name, external, selection: selected);
            if (Figures.SelectedItem == selected) break;
        }
    }

    private void RestoreFigureSelection()
    {
        restoringFigure = true;
        try { Figures.SelectedItem = figureCards.FirstOrDefault(f => f.Name == shownFigure); }
        finally { restoringFigure = false; }
    }

    private async Task RenderFigure(string name, bool external = false, bool force = false, bool? dark = null,
        FigureCard? selection = null)
    {
        var client = engine!;
        bool Obsolete() => engine != client || selection is not null && Figures.SelectedItem != selection;
        bool switching = name != shownFigure;
        double density = switching || !validFigure
            ? figureCards.FirstOrDefault(card => card.Name == name)?.PreviewDensity ?? InitialPreviewDensity
            : force ? PreviewDensityForView() : CurrentPreviewDensity;
        try
        {
            var render = await client.CallAsync<FigureRender>("figure", new { name, dark = dark ?? darkPreview, force }, density);
            if (Obsolete()) return;
            if (dark is { } value) darkPreview = value;
            if (force) Page.ClearSpriteCache();
            ShowRender(render, external);
        }
        catch when (Obsolete()) { }
        catch
        {
            if (switching && shownFigure != name)
            {
                RestoreFigureSelection();
                if (Figures.SelectedItem is null) ClearFigure();
            }
            throw;
        }
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
        EmptyCanvasCard.Visibility = Visibility.Visible;
        EmptyCanvas.Text = project is null ? "Open a local or SSH project, or start a new one." : "Create or select a figure to get started.";
        CanvasSize.Text = "";
        ClearCode();
        RefreshCaption(null);
        if (state is null) RefreshHistory();
        SyncGeometry();
        ShowWarnings(null);
    }

    private void ShowRender(FigureRender render, bool external = false)
    {
        string source = File.Exists(render.File) ? NormalizeSource(File.ReadAllText(render.File)) : "";
        // A source change can reorder the OIDs. Asset-only changes and duplicate notifications can keep the selection.
        bool preserve = shownFigure == render.Figure && (!external || source == renderedSource);
        renderedSource = source;
        shownFigure = render.Figure;
        currentRender = render;
        validFigure = render.Error is null && render.Size is [> 0, > 0];
        Page.ShowFigure(validFigure ? render : null, preserve);
        RefreshLayers();
        UpdateZoom();
        PageView.Visibility = validFigure ? Visibility.Visible : Visibility.Collapsed;
        ScheduleRulers();
        EmptyCanvasCard.Visibility = validFigure ? Visibility.Collapsed : Visibility.Visible;
        EmptyCanvas.Text = validFigure ? "" : "The figure could not be rendered. See the status below.";
        CanvasSize.Text = validFigure ? $"{render.Size![0]:g} × {render.Size[1]:g} mm" : "";
        UpdateFigureCard(render.Figure, render.Thumb, render.Error, render.Warnings?.Count(w => w.Level != "info") ?? 0,
            validFigure ? render.PxPerMM : null);
        RefreshCode(render, source);
        RefreshCaption(render);
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
            : new { figure = name, op = "move", oids = edit.Oids, dx = edit.Delta.X, dy = edit.Delta.Y, labels = edit.MoveAllLabels ? "all" : null };
        return EditFigure(name, args, "Updating figure…");
    }

    private Task EditFigure(string figure, object args, string status, bool clearSelection = false) => During(status, async () =>
    {
        var result = await CallForCurrentFigure<EditResult>("edit", args);
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

    private Task ExportFigure(string figure, string path, int dpi, string journal) =>
        ExportWithOptions(figure, path, dpi, journal, false, false, false, 0, false);

    private Task ExportWithOptions(string figure, string path, int dpi, string journal, bool dark, bool cmyk,
                                   bool outline, double bleed, bool marks) => During("Exporting figure…", async () =>
    {
        string format = Path.GetExtension(path).ToLowerInvariant();
        if (format is not (".pdf" or ".svg" or ".png" or ".tiff" or ".pptx" or ".eps" or ".jpg" or ".jpeg"))
            throw new ArgumentException("Choose a supported figure extension.");
        var result = await engine!.CallAsync<ExportResult>("export", new { figure, path, format = format[1..], dpi,
            dark, cmyk, outline, bleed, marks, journal = string.IsNullOrEmpty(journal) ? null : journal });
        var warnings = result.Warnings ?? [];
        var notes = result.Resolution?.Notes ?? [];
        if (warnings.Length + notes.Length > 0)
        {
            ShowExportMessages(warnings, notes);
            ShowDrawer(PreflightTab);
        }
        string density = result.Resolution?.Dpi is { } actual && format is ".png" or ".tiff" or ".jpg" or ".jpeg"
            ? $" · {actual} dpi" : "";
        Status.Text = $"Exported {path}" + density + (warnings.Length > 0 ? $" · {warnings.Length} warning(s); see Preflight." : "");
    });

    private async void HandleKey(object sender, KeyEventArgs e)
    {
        if (Page.IsKeyboardFocusWithin && e.Key == Key.Space) { e.Handled = true; return; }
        if (SourceText.IsKeyboardFocusWithin || (TerminalRequested && TerminalHost.IsKeyboardFocusWithin) ||
            Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        var modifiers = Keyboard.Modifiers;
        if (!busy && MenuShortcut(e.Key, modifiers) is { } menuTask) { e.Handled = true; await menuTask; return; }
        if (!busy && DrawingShortcut(e.Key, modifiers)) { e.Handled = true; return; }
        if (!busy && e.Key == Key.Escape && Page.Tool != CanvasTool.Select && Page.IsKeyboardFocusWithin)
        { Page.SetTool(CanvasTool.Select); e.Handled = true; return; }
        if (!busy &&
            FigureShortcut(e.Key, modifiers) is { } shortcut) { e.Handled = true; await shortcut; return; }
        if (e.Key == Key.Escape && !busy && Page.CancelCrop()) { e.Handled = true; return; }
        if (e.Key == Key.Escape && !busy && !Page.IsDragging && Page.ClearPanelPart()) { e.Handled = true; return; }
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
