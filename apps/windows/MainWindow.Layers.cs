using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private bool syncingLayers;
    private double zoom = 1;
    private (Point Point, double X, double Y, MouseButton Button)? pan;
    private sealed record LayerRow(string Oid, string Title, string Glyph, bool Nested, bool Locked, bool Hidden, string? Error)
    {
        public Thickness Indent => new(Nested ? 13 : 0, 0, 0, 0);
        public Visibility ToggleVisibility => Nested ? Visibility.Collapsed : Visibility.Visible;
        public string Eye => Hidden ? "\uED1A" : "\uE890";
        public string Lock => Locked ? "\uE72E" : "\uE785";
        public string VisibilityAction => Hidden ? "Show layer" : "Hide layer";
        public string LockAction => Locked ? "Unlock layer" : "Lock layer";
        public string Hint => Error is null ? Title : $"{Title}\n{Error}";
        public string GlyphTint => Error is null ? "#546166" : "#AA2020";
    }

    private void RefreshLayers()
    {
        var render = validFigure ? currentRender : null;
        var labels = (render?.Labels ?? []).Where(l => l.Panel is not null)
            .ToLookup(l => l.Panel!, l => l.Text);
        var elements = render?.Elements ?? [];
        var children = elements.Where(e => e.Parent is not null).ToLookup(e => e.Parent!);
        IEnumerable<FigureElement> WithChildren(FigureElement element)
        {
            yield return element;
            foreach (var child in children[element.Oid].Reverse())
                foreach (var row in WithChildren(child)) yield return row;
        }
        var rows = elements.Where(FigureCanvas.TopLevel).Reverse().SelectMany(WithChildren).Select(e =>
        {
            string? name = e.Props?.Name ?? (e.Props?.Source is { } source ? Path.GetFileName(source) : null);
            if (string.IsNullOrWhiteSpace(name) && e.Kind is "text" or "bracket") name = e.Props?.Text?.Split('\n')[0];
            string title = e.Method ?? e.Kind ?? "element";
            string? label = labels[e.Oid].FirstOrDefault();
            if (!string.IsNullOrEmpty(label)) title = $"{label} · {title}";
            if (!string.IsNullOrWhiteSpace(name)) title += $": {name}";
            if (e.Group is { } group) title += $" [{group}]";
            if (e.Props is { Hidden: true }) title += " [hidden]";
            if (e.Props is { Locked: true }) title += " [locked]";
            string glyph = e.Kind switch
            {
                "image" => "\uEB9F", "panel" => "\uE9D9", "text" => "\uE8D2", "table" => "\uE809",
                "line" => "\uE7AD", "layout" or "figure" => "\uECA5", _ => "\uE91F"
            };
            return new LayerRow(e.Oid, title, glyph, !FigureCanvas.TopLevel(e), e.Props?.Locked == true,
                e.Props?.Hidden == true, e.Error);
        }).ToArray();
        syncingLayers = true;
        try
        {
            if (!Layers.Items.Cast<LayerRow>().SequenceEqual(rows)) Layers.ItemsSource = rows;
            CopySelectionToLayers();
        }
        finally { syncingLayers = false; }
    }

    private void CopySelectionToLayers()
    {
        var wanted = Layers.Items.Cast<LayerRow>().Where(r => Page.SelectedIds.Contains(r.Oid)).ToArray();
        if (Layers.SelectedItems.Cast<LayerRow>().SequenceEqual(wanted)) return;
        Layers.SelectedItems.Clear();
        foreach (var row in wanted) Layers.SelectedItems.Add(row);
        if (wanted.Length == 1) Layers.ScrollIntoView(wanted[0]);
    }

    private void CanvasSelectionChanged()
    {
        if (!syncingLayers)
        {
            syncingLayers = true;
            try { CopySelectionToLayers(); }
            finally { syncingLayers = false; }
        }
        SyncGeometry();
        UpdateLayerControls();
    }

    private void LayersChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingLayers) return;
        syncingLayers = true;
        try
        {
            var ids = Layers.SelectedItems.Cast<LayerRow>().Select(r => r.Oid).ToHashSet();
            // Preserve click order for the reference layer, even if WPF reports rows in z-order.
            Page.SelectIds(Page.SelectedIds.Where(ids.Contains).Concat(e.AddedItems.Cast<LayerRow>().Select(r => r.Oid)));
        }
        finally { syncingLayers = false; }
    }

    private void UpdateLayerControls()
    {
        bool ready = !busy && validFigure;
        Layers.IsEnabled = ready;
        bool hasSelection = ready && Page.SelectedIds.Count > 0 && Page.Selections.All(e => FigureCanvas.TopLevel(e) && e.Kind != "label");
        FrontButton.IsEnabled = ForwardButton.IsEnabled = BackwardButton.IsEnabled = BackButton.IsEnabled = hasSelection;
        var selected = Page.Selection;
        VisibilityButton.IsEnabled = LockButton.IsEnabled = ready && selected is not null && FigureCanvas.TopLevel(selected) && selected.Kind != "label";
        VisibilityButton.Content = selected?.Props?.Hidden == true ? "Show" : "Hide";
        LockButton.Content = selected?.Props?.Locked == true ? "Unlock" : "Lock";
        ArrangeButton.IsEnabled = ready && Page.SelectedIds.Count > 0;
        UpdateArrangementControls();
    }

    private async void LayerButtonClicked(object sender, RoutedEventArgs e) => await LayerOrder((string)((Button)sender).Tag);

    private Task LayerOrder(string how) => shownFigure is not { } figure || Page.SelectedIds.Count == 0 || !Page.Selections.All(e => FigureCanvas.TopLevel(e) && e.Kind != "label") ? Task.CompletedTask :
        EditFigure(figure, new { figure, op = "order", oids = Page.SelectedIds, how }, "Reordering layers…", clearSelection: true);

    private async void VisibilityClicked(object sender, RoutedEventArgs e) => await ToggleLayer("hidden");
    private async void LockClicked(object sender, RoutedEventArgs e) => await ToggleLayer("locked");

    private async void RowLayerToggle(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LayerRow row, Tag: string property } &&
            currentRender?.Elements?.FirstOrDefault(el => el.Oid == row.Oid) is { } layer)
            await ToggleLayer(property, layer);
    }

    private Task ToggleLayer(string property) => ToggleLayer(property, Page.Selection);

    private Task ToggleLayer(string property, FigureElement? selected)
    {
        if (busy || shownFigure is not { } figure || selected is null || !FigureCanvas.TopLevel(selected) || selected.Kind == "label") return Task.CompletedTask;
        bool enabled = property == "hidden" ? selected.Props?.Hidden == true : selected.Props?.Locked == true;
        return EditFigure(figure, new { figure, op = "set", oid = selected.Oid,
            props = new Dictionary<string, bool> { [property] = !enabled } }, "Updating layer…");
    }

    private async void LayersDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Layers, e.OriginalSource as DependencyObject) is not ListBoxItem) return;
        if (Page.Selection is { } layer && (!FigureCanvas.TopLevel(layer) || layer.Kind == "panel")) await EditSource();
        else if (DetailsButton.IsEnabled && DetailsButton.Visibility == Visibility.Visible) await EditDetails();
    }

    private void ArrangeClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = sender as Button ?? ArrangeButton, Placement = PlacementMode.Bottom };
        AddArrangementMenu(menu, track: false);
        menu.Items.Add(new Separator());
        foreach (var (label, op, keys) in new[] { ("Duplicate", "duplicate", "Ctrl+D"),
            ("Group", "group", "Ctrl+G"), ("Ungroup", "ungroup", "Ctrl+Shift+G"), ("Remove", "delete", "Del") })
        {
            var item = new MenuItem { Header = label, InputGestureText = keys, IsEnabled = CanEditLayers(op) };
            item.Click += async (_, _) => await EditLayers(op);
            menu.Items.Add(item);
        }
        var make = new MenuItem { Header = "Make patchwork layout", IsEnabled = CanMakeLayout() };
        make.Click += async (_, _) => await MakeLayout();
        var release = new MenuItem { Header = "Release layout", IsEnabled = Page.Selection is { Kind: "layout" } layer && FigureCanvas.Unlocked(layer) };
        release.Click += async (_, _) => await ReleaseLayout();
        menu.Items.Add(make);
        menu.Items.Add(release);
        menu.IsOpen = true;
    }

    private bool CanEditLayers(string op)
    {
        var selection = Page.Selections;
        if (selection.Length == 0 || selection.Any(e => !FigureCanvas.Unlocked(e))) return false;
        return op switch
        {
            "group" => selection.Length >= 2 && selection.All(e => FigureCanvas.Movable(e) && e.Group is null),
            "ungroup" => selection.Any(e => e.Group is not null),
            "duplicate" => selection.All(e => FigureCanvas.Movable(e) && e.Kind is not ("layout" or "path")),
            _ => true
        };
    }

    private Task EditLayers(string op)
    {
        if (!validFigure || shownFigure is not { } figure || !CanEditLayers(op)) return Task.CompletedTask;
        var selection = Page.Selections;
        var oids = op == "ungroup" ? Page.IncludeGroupMembers(selection) : selection.Select(e => e.Oid).ToArray();
        return EditFigure(figure, new { figure, op, oids }, "Editing layers…", clearSelection: op == "delete");
    }

    private Task? FigureShortcut(Key key, ModifierKeys mods)
    {
        bool editingCanvas = Page.IsKeyboardFocusWithin || Layers.IsKeyboardFocusWithin;
        if (editingCanvas && mods == ModifierKeys.Control && key == Key.A && validFigure)
        { Page.SelectAll(); return Task.CompletedTask; }
        if (editingCanvas && mods == ModifierKeys.Control && key == Key.C && validFigure) return CopyFromCanvas();
        if (editingCanvas && mods == ModifierKeys.Control && key == Key.X && CanEditLayers("delete")) return CutLayers();
        if (editingCanvas && mods == ModifierKeys.Control && key == Key.V && validFigure) return PasteIntoCanvas();
        if (mods == ModifierKeys.Control && key == Key.D && CanEditLayers("duplicate")) return EditLayers("duplicate");
        if (mods == ModifierKeys.Control && key == Key.G && CanEditLayers("group")) return EditLayers("group");
        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.G && CanEditLayers("ungroup")) return EditLayers("ungroup");
        if (mods == ModifierKeys.None && key == Key.Delete && CanEditLayers("delete")) return EditLayers("delete");
        if (Layers.IsKeyboardFocusWithin || !Page.IsKeyboardFocusWithin || !validFigure || Page.IsDragging ||
            Page.SelectedIds.Count == 0 || !Page.CanMoveSelection()) return null;
        double step = mods switch { ModifierKeys.Shift => 5, ModifierKeys.Control => 0.1, ModifierKeys.None => 0.5, _ => 0 };
        if (step <= 0) return null;
        Vector delta = key switch { Key.Left => new(-step, 0), Key.Right => new(step, 0),
            Key.Up => new(0, -step), Key.Down => new(0, step), _ => new() };
        return delta.Length > 0 ? CanvasEdited(new(Page.SelectedIds.ToArray(), null, delta)) : null;
    }

    private sealed record ArrangeCommand(string Op, string Value, string Label, string Icon, double Rotation = 0);
    private static readonly ArrangeCommand[] AlignmentCommands =
    [
        new("align", "left", "Align left edges", "M1,0 V16 M4,3 H14 V6 H4 Z M4,10 H10 V13 H4 Z"),
        new("align", "hcenter", "Align horizontal centers", "M8,0 V16 M2,3 H14 V6 H2 Z M4,10 H12 V13 H4 Z"),
        new("align", "right", "Align right edges", "M15,0 V16 M2,3 H12 V6 H2 Z M6,10 H12 V13 H6 Z"),
        new("align", "top", "Align tops", "M1,0 V16 M4,3 H14 V6 H4 Z M4,10 H10 V13 H4 Z", 90),
        new("align", "vcenter", "Align vertical centers", "M8,0 V16 M2,3 H14 V6 H2 Z M4,10 H12 V13 H4 Z", 90),
        new("align", "bottom", "Align bottoms", "M15,0 V16 M2,3 H12 V6 H2 Z M6,10 H12 V13 H6 Z", 90),
    ];
    private static readonly ArrangeCommand[] DistributeCommands =
    [
        new("distribute", "h", "Distribute horizontally (equal gaps)", "M1,1 V15 M15,1 V15 M4,5 H6 V11 H4 Z M10,5 H12 V11 H10 Z"),
        new("distribute", "v", "Distribute vertically (equal gaps)", "M1,1 V15 M15,1 V15 M4,5 H6 V11 H4 Z M10,5 H12 V11 H10 Z", 90),
    ];
    private static readonly ArrangeCommand[] MatchCommands =
    [
        new("match", "width", "Match width to first selected", "M1,8 H15 M5,4 L1,8 L5,12 M11,4 L15,8 L11,12"),
        new("match", "height", "Match height to first selected", "M1,8 H15 M5,4 L1,8 L5,12 M11,4 L15,8 L11,12", 90),
    ];
    private readonly List<(ArrangeCommand Command, Control Control)> arrangementControls = [];

    private void SetupArrangement()
    {
        void AddButtons(Panel panel, IEnumerable<ArrangeCommand> commands)
        {
            foreach (var command in commands)
            {
                var icon = new ShapePath { Data = System.Windows.Media.Geometry.Parse(command.Icon), Width = 16, Height = 16,
                    Stroke = (Brush)FindResource("WorkspaceInk"), StrokeThickness = 1.25,
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(command.Rotation) };
                var button = new Button { Style = (Style)FindResource("ToolButton"), Content = icon,
                    ToolTip = command.Label, Width = 23, Padding = new Thickness(2), Margin = new Thickness(1, 0, 0, 0) };
                AutomationProperties.SetName(button, command.Label);
                button.Click += async (_, _) => await Arrange(command.Op, command.Value);
                panel.Children.Add(button);
                arrangementControls.Add((command, button));
            }
        }
        AddButtons(CanvasArrangeTools, AlignmentCommands);
        CanvasArrangeTools.Children.Add(new Border { Width = 1, Margin = new Thickness(5, 4, 4, 4),
            Background = (Brush)FindResource("WorkspaceLine") });
        AddButtons(CanvasArrangeTools, DistributeCommands);
        AddButtons(InspectorAlignTools, AlignmentCommands);
        AddButtons(InspectorDistributeTools, DistributeCommands.Concat(MatchCommands));
        AddArrangementMenu(ObjectMenu, track: true);
        FixedGap.TextChanged += (_, _) => UpdateFixedGapControls();
    }

    private void AddArrangementMenu(ItemsControl menu, bool track)
    {
        void Section(string name, ArrangeCommand[] commands)
        {
            var section = new MenuItem { Header = name };
            foreach (var command in commands)
            {
                var item = new MenuItem { Header = command.Label, IsEnabled = CanArrange(command.Op) };
                item.Click += async (_, _) => await Arrange(command.Op, command.Value);
                section.Items.Add(item);
                if (track) arrangementControls.Add((command, item));
            }
            menu.Items.Add(section);
        }
        Section("Align", AlignmentCommands);
        Section("Distribute", DistributeCommands);
        Section("Match Size", MatchCommands);
    }

    private bool CanArrange(string operation)
    {
        if (busy || !validFigure || Page.IsDragging) return false;
        var selection = Page.Selections;
        int required = operation switch { "align" => 1, "distribute" => 3, _ => 2 };
        return selection.Length >= required && selection.All(FigureCanvas.Movable) &&
            (operation != "match" || selection.Any(e => e.Oid != Page.SelectedIds[0] &&
                e.Kind is "panel" or "image" or "rect" or "ellipse" or "layout"));
    }

    private void UpdateArrangementControls()
    {
        var enabled = arrangementControls.Select(pair => pair.Command.Op).Distinct().ToDictionary(op => op, CanArrange);
        foreach (var (command, control) in arrangementControls) control.IsEnabled = enabled[command.Op];
        int count = Page.SelectedIds.Count;
        InspectorArrange.Visibility = validFigure && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlignTitle.Text = count == 1 ? "ALIGN TO FIGURE" : "ALIGN";
        AlignmentTarget.Visibility = InspectorDistribute.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        AlignmentTarget.IsEnabled = CanArrange("align");
        UpdateFixedGapControls();
    }

    private void UpdateFixedGapControls() => FixedAcross.IsEnabled = FixedDown.IsEnabled =
        CanArrange("fixed") && TryMm(FixedGap.Text, false, out _);

    private async void FixedGapClicked(object sender, RoutedEventArgs e)
    {
        if (TryMm(FixedGap.Text, false, out double gap))
            await Arrange("fixed", (string)((Button)sender).Tag, gap);
    }

    private Task Arrange(string op, string value, double? gap = null)
    {
        if (shownFigure is not { } figure || !CanArrange(op)) return Task.CompletedTask;
        var oids = Page.SelectedIds.ToArray();
        string to = oids.Length == 1 ? "figure" : (AlignmentTarget.SelectedItem as ComboBoxItem)?.Tag as string ?? "selection";
        object args = op switch
        {
            "align" => new { figure, op, oids, how = value, to, key = oids[0] },
            "match" => new { figure, op, oids, what = value },
            "fixed" => new { figure, op = "distribute", oids, axis = value, gap },
            _ => new { figure, op = "distribute", oids, axis = value }
        };
        return EditFigure(figure, args, "Arranging layers…");
    }

    private void ViewportResized(object sender, SizeChangedEventArgs e) => UpdateZoom();
    private void ViewportWheel(object sender, MouseWheelEventArgs e)
    {
        if (!validFigure || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ZoomAround(e.GetPosition(Page), Math.Clamp(zoom * (e.Delta > 0 ? 1.35 : 1 / 1.35), 0.4, 16), e.GetPosition(Viewport));
        e.Handled = true;
    }
    private void ZoomClicked(object sender, RoutedEventArgs e) => ChangeZoom((string)((Button)sender).Tag);

    private void PanStarted(object sender, MouseButtonEventArgs e)
    {
        bool spaceDrag = e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space) && Page.IsKeyboardFocusWithin;
        if ((e.ChangedButton != MouseButton.Middle && !spaceDrag) || !validFigure || Page.IsDragging || !Viewport.CaptureMouse()) return;
        pan = (e.GetPosition(Viewport), Viewport.HorizontalOffset, Viewport.VerticalOffset, e.ChangedButton);
        Viewport.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private void PanMoved(object sender, MouseEventArgs e)
    {
        if (pan is not { } start) return;
        Vector delta = e.GetPosition(Viewport) - start.Point;
        Viewport.ScrollToHorizontalOffset(start.X - delta.X);
        Viewport.ScrollToVerticalOffset(start.Y - delta.Y);
        e.Handled = true;
    }

    private void PanEnded(object sender, MouseButtonEventArgs e)
    {
        if (pan is not { } current || e.ChangedButton != current.Button) return;
        Viewport.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PanLost(object sender, MouseEventArgs e)
    {
        pan = null;
        Viewport.ClearValue(CursorProperty);
    }

    private void ChangeZoom(string command)
    {
        if (!validFigure) return;
        double centerX = (Viewport.HorizontalOffset + Viewport.ViewportWidth / 2) / Math.Max(1, Viewport.ExtentWidth);
        double centerY = (Viewport.VerticalOffset + Viewport.ViewportHeight / 2) / Math.Max(1, Viewport.ExtentHeight);
        zoom = command switch { "in" => Math.Min(16, zoom * 1.35), "out" => Math.Max(0.4, zoom / 1.35),
            "actual" => 96 / 25.4 / FitScale(Page.Width, Page.Height), _ => 1 };
        UpdateZoom();
        Viewport.UpdateLayout();
        Viewport.ScrollToHorizontalOffset(centerX * Viewport.ExtentWidth - Viewport.ViewportWidth / 2);
        Viewport.ScrollToVerticalOffset(centerY * Viewport.ExtentHeight - Viewport.ViewportHeight / 2);
    }

    private void UpdateZoom()
    {
        if (!validFigure || currentRender?.Size is not [var w, var h] || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
        double scale = zoom * FitScale(w, h);
        PageView.Width = w * scale;
        PageView.Height = h * scale;
        PageView.Effect = zoom <= 1 ? PaperShadow : null;
        ZoomText.Text = zoom == 1 ? "Fit" : $"{zoom:P0} of fit";
    }

    private double FitScale(double width, double height) => Math.Min(Math.Max(1, Viewport.ActualWidth - 30) / width,
        Math.Max(1, Viewport.ActualHeight - 30) / height);
}
