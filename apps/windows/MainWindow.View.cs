using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private sealed record ViewPreferences(bool Grid, bool GridSnap, bool Guides, bool Smart, double Step, bool Sidebar, bool Inspector,
        string? Unit = null, double? SidebarWidth = null, double? InspectorWidth = null, double? DrawerHeight = null);
    private sealed record WindowFrame(double X, double Y, double Width, double Height, bool Maximized);
    private string geometryUnit = "mm";
    private double MillimetersPerUnit => geometryUnit switch { "in" => 25.4, "pt" => 25.4 / 72, _ => 1 };
    private string GeometryNumber(double? mm) => FormatMm(mm / MillimetersPerUnit);
    private static readonly (double Step, string Label)[] GridSteps =
        [(0.5, "0.5 mm"), (1, "1 mm"), (2, "2 mm"), (5, "5 mm"), (10, "10 mm"), (12.7, "0.5 in (12.7 mm)"), (25.4, "1 in (25.4 mm)")];
    private string ViewFile => Path.Combine(settingsDirectory, "view.json");
    private GridLength sidebarWidth = new(196), inspectorWidth = new(237);

    private void SetupViewPreferences()
    {
        try
        {
            if (File.Exists(ViewFile) && JsonSerializer.Deserialize<ViewPreferences>(File.ReadAllText(ViewFile)) is { } p)
            {
                Page.ShowGrid = p.Grid;
                Page.SnapGrid = p.GridSnap;
                Page.ShowGuides = p.Guides;
                Page.SnapEnabled = p.Smart;
                Page.GridStep = GridSteps.Any(s => s.Step == p.Step) ? p.Step : 1;
                geometryUnit = p.Unit is "in" or "pt" ? p.Unit : "mm";
                if (p.SidebarWidth is >= 125 and <= 600) SideColumn.Width = sidebarWidth = new GridLength(p.SidebarWidth.Value);
                if (p.InspectorWidth is >= 172 and <= 700) InspectorColumn.Width = inspectorWidth = new GridLength(p.InspectorWidth.Value);
                if (p.DrawerHeight is >= 75 and <= 700) DrawerRow.Height = drawerHeight = new GridLength(p.DrawerHeight.Value);
                SetSidePane(true, p.Sidebar);
                SetSidePane(false, p.Inspector);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void SaveViewPreferences()
    {
        var preferences = new ViewPreferences(Page.ShowGrid, Page.SnapGrid, Page.ShowGuides, Page.SnapEnabled, Page.GridStep,
            SidePanel.Visibility == Visibility.Visible, InspectorPane.Visibility == Visibility.Visible, geometryUnit,
            SidePanel.Visibility == Visibility.Visible ? SideColumn.ActualWidth : sidebarWidth.Value,
            InspectorPane.Visibility == Visibility.Visible ? InspectorColumn.ActualWidth : inspectorWidth.Value,
            DrawerTabs.Visibility == Visibility.Visible ? DrawerRow.ActualHeight : drawerHeight.Value);
        try { SaveSetting(ViewFile, preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "Could not save view preferences: " + ex.Message; }
    }

    private void RestoreWindowFrame()
    {
        try
        {
            string path = Path.Combine(settingsDirectory, "window.json");
            if (!File.Exists(path) || JsonSerializer.Deserialize<WindowFrame>(File.ReadAllText(path)) is not { } p) return;
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Width) || !double.IsFinite(p.Height)) return;
            var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var work = SystemParameters.WorkArea;
            Width = Math.Clamp(p.Width, MinWidth, Math.Max(MinWidth, desktop.Width));
            Height = Math.Clamp(p.Height, MinHeight, Math.Max(MinHeight, desktop.Height));
            if (desktop.Contains(new Rect(p.X, p.Y, Math.Min(160, Width), 50)))
            { Left = p.X; Top = p.Y; }
            else
            {
                Width = Math.Min(Width, Math.Max(work.Width, MinWidth));
                Height = Math.Min(Height, Math.Max(work.Height, MinHeight));
                Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
                Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
            }
            if (p.Maximized) WindowState = WindowState.Maximized;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void SaveWindowFrame()
    {
        var bounds = RestoreBounds;
        if (bounds.IsEmpty || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)) return;
        try { SaveSetting(Path.Combine(settingsDirectory, "window.json"), new WindowFrame(bounds.X, bounds.Y,
            bounds.Width, bounds.Height, WindowState == WindowState.Maximized)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private void UpdateViewAid(Action change)
    {
        change();
        Page.InvalidateVisual();
        ScheduleRulers();
        SaveViewPreferences();
    }

    private void FillViewAids(ItemsControl menu)
    {
        MenuAction(menu, "Grid Lines", () => UpdateViewAid(() => Page.ShowGrid = !Page.ShowGrid)).IsChecked = Page.ShowGrid;
        var spacing = new MenuItem { Header = "Grid Spacing" };
        foreach (var (step, label) in GridSteps)
            MenuAction(spacing, label, () => UpdateViewAid(() => Page.GridStep = step)).IsChecked = Page.GridStep == step;
        menu.Items.Add(spacing);
        MenuAction(menu, "Snap to Grid", () => UpdateViewAid(() => Page.SnapGrid = !Page.SnapGrid)).IsChecked = Page.SnapGrid;
        MenuAction(menu, "Guides", () => UpdateViewAid(() => Page.ShowGuides = !Page.ShowGuides)).IsChecked = Page.ShowGuides;
        var units = new MenuItem { Header = "Inspector Geometry Units" };
        foreach (string unit in new[] { "mm", "in", "pt" })
            MenuAction(units, unit, () => { geometryUnit = unit; SyncGeometry(); SaveViewPreferences(); }).IsChecked = geometryUnit == unit;
        menu.Items.Add(units);
    }

    private void ToggleSidePane(bool sidebar)
    {
        FrameworkElement panel = sidebar ? SidePanel : InspectorPane;
        SetSidePane(sidebar, panel.Visibility != Visibility.Visible);
        SaveViewPreferences();
    }

    private void SetSidePane(bool sidebar, bool visible)
    {
        var column = sidebar ? SideColumn : InspectorColumn;
        var divider = sidebar ? SideSplitColumn : InspectorSplitColumn;
        FrameworkElement panel = sidebar ? SidePanel : InspectorPane;
        FrameworkElement splitter = sidebar ? SideSplitter : InspectorSplitter;
        ref GridLength prior = ref (sidebar ? ref sidebarWidth : ref inspectorWidth);
        if (!visible && panel.Visibility == Visibility.Visible) prior = column.Width;
        panel.Visibility = splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        column.MinWidth = visible ? sidebar ? 125 : 172 : 0;
        column.Width = visible ? prior : new GridLength(0);
        divider.Width = new GridLength(visible ? 4 : 0);
    }

    private Task SetLabels(object props) => validFigure && shownFigure is { } figure
        ? EditFigure(figure, new { figure, op = "labels", props }, "Updating panel lettering…") : Task.CompletedTask;
    private string? LabelTextProperty(string name) => currentRender?.LabelStyle?.Props.TryGetValue(name, out var v) == true &&
        v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private async Task ChooseJournalLabels()
    {
        JournalChoice[] choices = [];
        await During("Loading journal lettering presets…", async () => choices = (await Journals()).Where(j => j.Labels is not null).ToArray());
        if (choices.Length == 0 || !validFigure) return;
        var dialog = new FormDialog(this, "Journal lettering convention");
        var selected = dialog.Choice("Journal", choices, choices.FirstOrDefault(j => j.Key == currentRender?.Journal?.Key) ?? choices[0]);
        if (dialog.Confirm("Apply", () => null) && selected.SelectedItem is JournalChoice { Labels: { } labels })
        {
            var changes = new Dictionary<string, JsonElement>(labels);
            if (LabelTextProperty("numbering") == "none") changes["numbering"] = JsonSerializer.SerializeToElement("alpha");
            await SetLabels(changes);
        }
    }

    private void FillLabelStyles(ItemsControl menu)
    {
        var props = currentRender?.LabelStyle?.Props;
        var numbering = LabelTextProperty("numbering");
        bool aligned = props is null || !props.TryGetValue("align", out var flag) || flag.ValueKind != JsonValueKind.False;
        bool sourceOrder = LabelTextProperty("order") == "source";
        double size = props?.TryGetValue("size", out var n) == true && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : 8;
        bool enabled = !busy && validFigure;
        foreach (var (sample, format, letterCase) in new[] { ("a", "{}", "lower"), ("(a)", "({})", "lower"),
                     ("a)", "{})", "lower"), ("a.", "{}.", "lower"), ("A", "{}", "upper"), ("(A)", "({})", "upper"),
                     ("A)", "{})", "upper"), ("A.", "{}.", "upper") })
            MenuAction(menu, $"Style {sample}", () => SetLabels(numbering == "none" ?
                (object)new { format, @case = letterCase, numbering = "alpha" } : new { format, @case = letterCase }), enabled)
                .IsChecked = numbering != "none" && LabelTextProperty("format") == format && LabelTextProperty("case") == letterCase;
        MenuAction(menu, "No Letters", () => SetLabels(new { numbering = "none" }), enabled).IsChecked = numbering == "none";
        MenuAction(menu, "Journal Convention…", ChooseJournalLabels, enabled);
        menu.Items.Add(new Separator());
        double offset = -(size * 0.35 + 0.5);
        MenuAction(menu, "Labels Outside Panels (above left)", () => SetLabels(new { dx = offset, dy = offset }), enabled);
        MenuAction(menu, "Labels at Panel Corners", () => SetLabels(new { dx = 0, dy = 0 }), enabled);
        MenuAction(menu, "Line Up Labels in Rows and Columns", () => SetLabels(new { align = !aligned }), enabled).IsChecked = aligned;
        MenuAction(menu, "Letter in Code Order", () => SetLabels(new { order = sourceOrder ? "reading" : "source" }), enabled).IsChecked = sourceOrder;
        menu.Items.Add(new Separator());
    }
}
