using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private sealed record RecentProject(string Path, string? Host, string? Python, string? Figure, bool Interactive = false)
    {
        public string Label => Host is null ? Path : $"{Host}:{Path}";
    }
    private readonly List<RecentProject> recentProjects = [];
    private readonly string settingsDirectory;
    private string RecentFile => Path.Combine(settingsDirectory, "recent-projects.json");

    private void RememberProject(string? figure)
    {
        if (project is not { } folder) return;
        var remote = engine?.Remote;
        var entry = new RecentProject(remote?.Path ?? folder, remote?.Host, remote?.Python, figure, remote?.Interactive == true);
        recentProjects.RemoveAll(r => r.Host == entry.Host && string.Equals(r.Path, entry.Path,
            entry.Host is null ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        recentProjects.Insert(0, entry);
        if (recentProjects.Count > 12) recentProjects.RemoveAt(12);
        SaveRecents();
    }

    private void SaveRecents()
    {
        try { SaveSetting(RecentFile, recentProjects); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "Could not save recent projects: " + ex.Message; }
    }

    private static void SaveSetting<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private Task OpenRecent(RecentProject recent) => recent.Host is { } host
        ? OpenRemote(host, recent.Path, recent.Python ?? "python3", recent.Figure, recent.Interactive)
        : During("Opening recent project…", async () =>
        {
            if (ConfirmDiscardDrafts()) await OpenProjectCore(recent.Path, recent.Figure);
        });

    private void FillRecents(ItemsControl menu)
    {
        if (recentProjects.Count == 0) { MenuAction(menu, "No recent projects", () => { }, false); return; }
        foreach (var r in recentProjects)
            MenuAction(menu, LiteralMenuLabel(r.Label), () => OpenRecent(r), !busy);
        menu.Items.Add(new Separator());
        MenuAction(menu, "Clear Recent Projects", () => { recentProjects.Clear(); SaveRecents(); }, !busy);
    }

    private static string LiteralMenuLabel(string text) => text.Replace("_", "__");
    private static MenuItem MenuAction(ItemsControl menu, string label, Action run, bool enabled = true, string? keys = null) =>
        MenuAction(menu, label, () => { run(); return Task.CompletedTask; }, enabled, keys);

    private static MenuItem MenuAction(ItemsControl menu, string label, Func<Task> run, bool enabled = true, string? keys = null)
    {
        var item = new MenuItem { Header = label, IsEnabled = enabled, InputGestureText = keys ?? "" };
        item.Click += async (_, _) => await run();
        menu.Items.Add(item);
        return item;
    }

    private void SetupMenus()
    {
        try
        {
            if (File.Exists(RecentFile) && JsonSerializer.Deserialize<RecentProject[]>(File.ReadAllText(RecentFile)) is { } entries)
                recentProjects.AddRange(entries.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Path)).Take(12));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

        void Populate(MenuItem menu, Action<ItemsControl> fill)
        {
            fill(menu);
            menu.SubmenuOpened += (_, e) =>
            {
                if (e.Source != menu) return;
                menu.Items.Clear();
                fill(menu);
            };
        }
        Populate(FileMenu, menu =>
        {
            MenuAction(menu, "New Project…", () => NewProjectClicked(this, new RoutedEventArgs()), !busy, "Ctrl+Shift+N");
            MenuAction(menu, "New Figure…", () => NewFigureClicked(this, new RoutedEventArgs()), !busy && project is not null, "Ctrl+N");
            MenuAction(menu, "New Poster from Template…", ChoosePoster, !busy && project is not null);
            MenuAction(menu, "Open Project…", () => OpenClicked(this, new RoutedEventArgs()), !busy, "Ctrl+O");
            MenuAction(menu, "Open Project on SSH Server…", ChooseRemoteProject, !busy, "Ctrl+Shift+O");
            var recent = new MenuItem { Header = "Open Recent" };
            FillRecents(recent);
            menu.Items.Add(recent);
            menu.Items.Add(new Separator());
            MenuAction(menu, "Apply Code Changes", async () => { await SaveCode(); }, !busy && CodeDirty, "Ctrl+S");
            MenuAction(menu, engine?.Remote is null ? "Show Project in Explorer" : "Show Local Mirror in Explorer", RevealProject, project is not null);
            menu.Items.Add(new Separator());
            MenuAction(menu, "Export Figure…", () => ExportClicked(this, new RoutedEventArgs()), !busy && validFigure, "Ctrl+E");
            MenuAction(menu, "Export All Figures as PDF…", ChooseBatchFolder, !busy && state?.Figures is { Length: > 0 }, "Ctrl+Shift+E");
            MenuAction(menu, "Export Table…", ChooseTableExport, !busy && TableToExport is not null);
            menu.Items.Add(new Separator());
            MenuAction(menu, "Choose External Editor…", ChooseExternalEditor, !busy);
            MenuAction(menu, "Reveal Source File in Explorer", RevealSource, !busy && codePath is not null);
            MenuAction(menu, "Python Setup…", ChoosePython, !busy && engine?.Remote is null);
            MenuAction(menu, "Install Command Line Tool…", InstallCommandLineTool, !busy);
            MenuAction(menu, "Check Python Packages…", CheckPythonPackages, !busy && (engine?.Remote is null || engine.Running && project is not null));
            MenuAction(menu, "Close", Close, !busy, "Ctrl+W");
        });
        Populate(EditMenu, menu =>
        {
            MenuAction(menu, "Undo" + (state?.History?.LastOrDefault() is { } last ? " " + LiteralMenuLabel(last) : ""), () => History("undo"), !busy && state?.CanUndo == true, "Ctrl+Z");
            MenuAction(menu, "Redo" + (state?.Future?.FirstOrDefault() is { } next ? " " + LiteralMenuLabel(next) : ""), () => History("redo"), !busy && state?.CanRedo == true, "Ctrl+Y");
            menu.Items.Add(new Separator());
            MenuAction(menu, "Cut", CutLayers, !busy && validFigure && CanEditLayers("delete"), "Ctrl+X");
            MenuAction(menu, "Copy", CopyFromCanvas, !busy && validFigure, "Ctrl+C");
            MenuAction(menu, "Paste", PasteIntoCanvas, !busy && validFigure, "Ctrl+V");
            MenuAction(menu, "Copy Style", CopyLayerStyle, !busy && validFigure && Page.SelectedIds.Count > 0);
            MenuAction(menu, "Paste Style", PasteLayerStyle, CanPasteStyle);
            MenuAction(menu, "Copy Figure as Picture", CopyFigureImage, !busy && validFigure, "Ctrl+Shift+C");
            MenuAction(menu, "Paste Image", PasteFigureImage, !busy && validFigure);
            MenuAction(menu, "Duplicate", () => EditLayers("duplicate"), !busy && CanEditLayers("duplicate"), "Ctrl+D");
            MenuAction(menu, "Delete", () => EditLayers("delete"), !busy && CanEditLayers("delete"), "Del");
            MenuAction(menu, "Select All", Page.SelectAll, !busy && validFigure, "Ctrl+A");
        });
        Populate(InsertMenu, menu =>
        {
            MenuAction(menu, "New Panel from Code…", NewPanelFromCode, !busy && validFigure, "Ctrl+Shift+P");
            MenuAction(menu, "Sample Plot", AddSample, !busy && validFigure);
            MenuAction(menu, "Panel from Existing Code…", () => ChooseImport("Import Python panel", "Python panels|*.py"), !busy && validFigure);
            MenuAction(menu, "Picture…", () => ChooseImport("Place picture", "Pictures|*.png;*.jpg;*.jpeg;*.svg;*.pdf;*.tif;*.tiff"), !busy && validFigure, "Ctrl+Shift+I");
            MenuAction(menu, "Figure…", ChoosePlacedFigure, !busy && validFigure && state?.Figures is { Length: > 1 });
            MenuAction(menu, "New Table…", ChooseNewTable, !busy && project is not null, "Ctrl+Shift+T");
            MenuAction(menu, "Table from CSV or TSV…", () => ChooseImport("Import table", "Tables|*.csv;*.tsv"), !busy && validFigure);
            MenuAction(menu, "Panel from SSH Server…", ChooseRemotePanel, !busy && validFigure);
            MenuAction(menu, "BioCartoon Icons…", BrowseIcons, !busy && validFigure, "Ctrl+Shift+B");
            MenuAction(menu, "Scale Bar on Picture…", ChooseScaleBar, IsEditableImage);
            MenuAction(menu, "Zoom Box", AddImageInset, CanAddZoom);
            MenuAction(menu, "Annotation or QR Code…", () => AnnotationClicked(this, new RoutedEventArgs()), !busy && validFigure);
            menu.Items.Add(new Separator());
            foreach (var tool in DrawingTools.Skip(1)) MenuAction(menu, tool.Label, () => PickTool(tool.Tool), !busy && validFigure, tool.Key.ToString());
        });
        Populate(LabelsMenu, menu =>
        {
            FillLabelStyles(menu);
            MenuAction(menu, "Panel Lettering Style…", () => EditStyle(true), !busy && validFigure);
            MenuAction(menu, "Selected Panel Letter…", EditDetails, !busy && Page.Selection?.Kind is "panel" or "image");
        });
        Populate(ViewMenu, menu =>
        {
            foreach (var (label, cmd, keys) in new[] { ("Zoom In", "in", "Ctrl++"), ("Zoom Out", "out", "Ctrl+-"), ("Fit Figure", "fit", "Ctrl+0"), ("Actual Size", "actual", "Ctrl+1") })
                MenuAction(menu, label, () => ChangeZoom(cmd), !busy && validFigure, keys);
            menu.Items.Add(new Separator());
            FillViewAids(menu);
            MenuAction(menu, "Add Vertical Guide…", () => ChooseGuide("x"), !busy && validFigure);
            MenuAction(menu, "Add Horizontal Guide…", () => ChooseGuide("y"), !busy && validFigure);
            MenuAction(menu, "Clear Guides", () => ChangeGuide("guide_clear", "x", 0), !busy && currentRender?.Guides is { Length: > 0 });
            MenuAction(menu, "Smart Guides", () => UpdateViewAid(() => Page.SnapEnabled = !Page.SnapEnabled)).IsChecked = Page.SnapEnabled;
            MenuAction(menu, "Dark Version of Figure", () => SetDarkPreview(!darkPreview), !busy && validFigure).IsChecked = darkPreview;
            var vision = new MenuItem { Header = "Color Vision", IsEnabled = !busy && validFigure };
            string[] modes = ["Normal", "Deuteranopia", "Protanopia", "Tritanopia", "Grayscale"];
            for (int i = 0; i < modes.Length; i++)
            {
                int mode = i - 1;
                MenuAction(vision, modes[i], () => Page.SetVision(mode)).IsChecked = Page.VisionMode == mode;
            }
            menu.Items.Add(vision);
            menu.Items.Add(new Separator());
            MenuAction(menu, "Caption", ShowCaption, !busy && validFigure);
            MenuAction(menu, "Code", () => ShowDrawer(CodeTab), project is not null);
            MenuAction(menu, "Preflight", () => ShowDrawer(PreflightTab), project is not null);
            MenuAction(menu, "History", ShowHistory, !busy && state is not null);
            MenuAction(menu, "Terminal", () => ShowDrawer(TerminalTab), !busy && project is not null);
            MenuAction(menu, "New Terminal", OpenNewShell, !busy && project is not null);
            menu.Items.Add(new Separator());
            MenuAction(menu, SidePanel.Visibility == Visibility.Visible ? "Hide Figures and Layers" : "Show Figures and Layers", () => ToggleSidePane(true));
            MenuAction(menu, InspectorPane.Visibility == Visibility.Visible ? "Hide Inspector" : "Show Inspector", () => ToggleSidePane(false));
            MenuAction(menu, "Render Again", RenderAgain, !busy && project is not null, "Ctrl+R");
        });
        Populate(WindowMenu, menu =>
        {
            MenuAction(menu, "Minimize", () => WindowState = WindowState.Minimized);
            MenuAction(menu, "About Fignimble", () => MessageBox.Show(this,
                $"Fignimble for Windows {FileVersionInfo.GetVersionInfo(typeof(MainWindow).Assembly.Location).ProductVersion}\n\nNative editor for Pachter Lab’s Fignimble engine.", "About Fignimble", MessageBoxButton.OK, MessageBoxImage.Information));
        });
        Populate(ObjectMenu, menu =>
        {
            var stacking = new MenuItem { Header = "Arrange" };
            AddStackingMenu(stacking);
            menu.Items.Add(stacking);
            AddArrangementMenu(menu, track: false);
            menu.Items.Add(new Separator());
            MenuAction(menu, "Group", () => EditLayers("group"), !busy && CanEditLayers("group"), "Ctrl+G");
            MenuAction(menu, "Ungroup", () => EditLayers("ungroup"), !busy && CanEditLayers("ungroup"), "Ctrl+Shift+G");
            var selectedLabel = SelectedPanelLabel;
            MenuAction(menu, selectedLabel?.Detached == true ? "Link Label to Panel" : "Unlink Label from Panel", ToggleLabelLink, !busy && selectedLabel is not null);
            MenuAction(menu, Page.Selection?.Props?.Locked == true ? "Unlock" : "Lock", () => ToggleLayer("locked"), !busy && Page.Selection is { Parent: null }, "Ctrl+2");
            MenuAction(menu, Page.Selection?.Props?.Hidden == true ? "Show" : "Hide", () => ToggleLayer("hidden"), !busy && Page.Selection is { Parent: null }, "Ctrl+3");
            MenuAction(menu, "Crop Picture", StartPictureCrop, IsEditableImage);
            MenuAction(menu, "Rotate Picture…", RotateImage, IsEditableImage);
            MenuAction(menu, "Rotate 90° Left", () => RotateSelected(90), CanRotateSelected);
            MenuAction(menu, "Rotate 90° Right", () => RotateSelected(-90), CanRotateSelected);
            MenuAction(menu, "Flip Horizontally", () => FlipPicture("h"), IsEditableImage);
            MenuAction(menu, "Flip Vertically", () => FlipPicture("v"), IsEditableImage);
            MenuAction(menu, "Make Layout", MakeLayout, !busy && CanMakeLayout(), "Ctrl+L");
            MenuAction(menu, "Release Layout", ReleaseLayout, CanReleaseLayout, "Ctrl+Shift+L");
            MenuAction(menu, "Edit Code", EditSource, !busy && currentRender is not null, "Ctrl+K");
        });
        Page.ContextMenu = new ContextMenu();
        Page.ContextMenuOpening += (_, e) =>
        {
            if (busy || !validFigure || Page.Tool != CanvasTool.Select) { e.Handled = true; return; }
            var menu = Page.ContextMenu;
            menu.Items.Clear();
            if (Page.SelectedIds.Count == 0)
            {
                MenuAction(menu, "New Panel from Code…", NewPanelFromCode);
                MenuAction(menu, "Place Picture…", () => ChooseImport("Place picture", "Pictures|*.png;*.jpg;*.jpeg;*.svg;*.pdf;*.tif;*.tiff"));
                MenuAction(menu, "Edit the Figure’s Code", EditSource);
                return;
            }
            MenuAction(menu, "Edit Code", EditSource);
            menu.Items.Add(new Separator());
            AddStackingMenu(menu);
            menu.Items.Add(new Separator());
            MenuAction(menu, "Group", () => EditLayers("group"), CanEditLayers("group"));
            MenuAction(menu, "Make Layout", MakeLayout, CanMakeLayout());
            MenuAction(menu, "Release Layout", ReleaseLayout, CanReleaseLayout);
            MenuAction(menu, "Ungroup", () => EditLayers("ungroup"), CanEditLayers("ungroup"));
            MenuAction(menu, "Duplicate", () => EditLayers("duplicate"), CanEditLayers("duplicate"));
            MenuAction(menu, "Delete", () => EditLayers("delete"), CanEditLayers("delete"));
            if (Page.Selection?.Kind == "table") MenuAction(menu, "Export Table…", ChooseTableExport);
            if (CanRefreshRemotePanel) MenuAction(menu, "Update from Server", RefreshRemotePanel);
            if (SelectedIcon() is { } icon) MenuAction(menu, "Show BioCartoon Credit…", () => ShowIconCredit(icon));
        };
    }

    private bool CanReleaseLayout => !busy && Page.Selection is { Kind: "layout" } e && FigureCanvas.Unlocked(e);
    private bool IsEditableImage => !busy && Page.Selection is { Kind: "image" } e && FigureCanvas.Unlocked(e);

    private void AddStackingMenu(ItemsControl menu)
    {
        foreach (var (label, how, keys) in new[] { ("Bring to Front", "front", "Ctrl+Shift+]"), ("Bring Forward", "forward", "Ctrl+]"),
                     ("Send Backward", "backward", "Ctrl+["), ("Send to Back", "back", "Ctrl+Shift+[") })
            MenuAction(menu, label, () => LayerOrder(how), !busy && validFigure && Page.SelectedIds.Count > 0 && Page.Selections.All(FigureCanvas.TopLevel), keys);
    }

    private void RevealProject()
    {
        if (project is not { } folder) return;
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
            info.ArgumentList.Add(folder);
            using var process = Process.Start(info);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private Task? MenuShortcut(Key key, ModifierKeys mods)
    {
        if (mods != ModifierKeys.Control && mods != (ModifierKeys.Control | ModifierKeys.Shift)) return null;
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        switch (key)
        {
            case Key.N:
                if (shift) NewProjectClicked(this, new RoutedEventArgs());
                else NewFigureClicked(this, new RoutedEventArgs());
                break;
            case Key.O:
                if (shift) return ChooseRemoteProject();
                OpenClicked(this, new RoutedEventArgs());
                break;
            case Key.E:
                if (shift) return ChooseBatchFolder();
                ExportClicked(this, new RoutedEventArgs());
                break;
            case Key.W when !shift: Close(); break;
            case Key.R when !shift: return RenderAgain();
            case Key.S when !shift: return SaveCode();
            case Key.K when !shift: return EditSource();
            case Key.L when shift && CanReleaseLayout: return ReleaseLayout();
            case Key.L when !shift && CanMakeLayout(): return MakeLayout();
            case Key.P when shift: return NewPanelFromCode();
            case Key.I when shift: return ChooseImport("Place picture", "Pictures|*.png;*.jpg;*.jpeg;*.svg;*.pdf;*.tif;*.tiff");
            case Key.B when shift && validFigure: return BrowseIcons();
            case Key.T when shift: return ChooseNewTable();
            case Key.C when shift && validFigure: return CopyFigureImage();
            case Key.OemOpenBrackets when validFigure: return LayerOrder(shift ? "back" : "backward");
            case Key.OemCloseBrackets when validFigure: return LayerOrder(shift ? "front" : "forward");
            case Key.D2 when !shift: return ToggleLayer("locked");
            case Key.D3 when !shift: return ToggleLayer("hidden");
            case Key.D0 when !shift: ChangeZoom("fit"); break;
            case Key.D1 when !shift: ChangeZoom("actual"); break;
            case Key.Add or Key.OemPlus: ChangeZoom("in"); break;
            case Key.Subtract or Key.OemMinus when !shift: ChangeZoom("out"); break;
            default: return null;
        }
        return Task.CompletedTask;
    }

    private void RecentClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = RecentButton };
        FillRecents(menu);
        menu.IsOpen = true;
    }
}
