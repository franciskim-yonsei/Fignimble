using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private bool syncingLayers;
    private double zoom = 1;
    private (Point Point, double X, double Y)? pan;
    private sealed record LayerRow(string Oid, string Title);

    private void RefreshLayers()
    {
        var render = validFigure ? currentRender : null;
        var labels = (render?.Labels ?? []).Where(l => l.Panel is not null)
            .ToLookup(l => l.Panel!, l => l.Text);
        var rows = (render?.Elements ?? []).Where(FigureCanvas.TopLevel).Reverse().Select(e =>
        {
            string? name = e.Props?.Name ?? (e.Props?.Source is { } source ? Path.GetFileName(source) : null);
            if (string.IsNullOrWhiteSpace(name) && e.Kind is "text" or "bracket") name = e.Props?.Text?.Split('\n')[0];
            string title = e.Method ?? e.Kind ?? "element";
            string? label = labels[e.Oid].FirstOrDefault();
            if (!string.IsNullOrEmpty(label)) title = $"{label} · {title}";
            if (!string.IsNullOrWhiteSpace(name)) title += $": {name}";
            if (e.Props is { Hidden: true }) title += " [hidden]";
            if (e.Props is { Locked: true }) title += " [locked]";
            return new LayerRow(e.Oid, title);
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
        try { Page.SelectIds(Layers.SelectedItems.Cast<LayerRow>().Select(r => r.Oid)); }
        finally { syncingLayers = false; }
    }

    private void UpdateLayerControls()
    {
        bool ready = !busy && validFigure;
        Layers.IsEnabled = ready;
        bool hasSelection = ready && Page.SelectedIds.Count > 0;
        FrontButton.IsEnabled = ForwardButton.IsEnabled = BackwardButton.IsEnabled = BackButton.IsEnabled = hasSelection;
        var selected = Page.Selection;
        VisibilityButton.IsEnabled = LockButton.IsEnabled = ready && selected is not null;
        VisibilityButton.Content = selected?.Props?.Hidden == true ? "Show" : "Hide";
        LockButton.Content = selected?.Props?.Locked == true ? "Unlock" : "Lock";
        ArrangeButton.IsEnabled = ready && Page.SelectedIds.Count >= 2 && Page.Selections.All(FigureCanvas.Movable);
    }

    private async void LayerButtonClicked(object sender, RoutedEventArgs e) => await LayerOrder((string)((Button)sender).Tag);

    private Task LayerOrder(string how) => shownFigure is not { } figure || Page.SelectedIds.Count == 0 ? Task.CompletedTask :
        EditFigure(figure, new { figure, op = "order", oids = Page.SelectedIds, how }, "Reordering layers…", clearSelection: true);

    private async void VisibilityClicked(object sender, RoutedEventArgs e) => await ToggleLayer("hidden");
    private async void LockClicked(object sender, RoutedEventArgs e) => await ToggleLayer("locked");

    private Task ToggleLayer(string property)
    {
        if (shownFigure is not { } figure || Page.Selection is not { } selected) return Task.CompletedTask;
        bool enabled = property == "hidden" ? selected.Props?.Hidden == true : selected.Props?.Locked == true;
        return EditFigure(figure, new { figure, op = "set", oid = selected.Oid,
            props = new Dictionary<string, bool> { [property] = !enabled } }, "Updating layer…");
    }

    private async void LayersDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Layers, e.OriginalSource as DependencyObject) is ListBoxItem &&
            DetailsButton.IsEnabled && DetailsButton.Visibility == Visibility.Visible)
            await EditDetails();
    }

    private void ArrangeClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ArrangeButton, Placement = PlacementMode.Bottom };
        foreach (var (text, value) in new[] { ("Align left", "left"), ("Align horizontal centers", "hcenter"),
            ("Align right", "right"), ("Align top", "top"), ("Align vertical centers", "vcenter"), ("Align bottom", "bottom") })
        {
            var item = new MenuItem { Header = text };
            item.Click += async (_, _) => await Arrange("align", value);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        foreach (var (text, axis) in new[] { ("Distribute horizontally", "h"), ("Distribute vertically", "v") })
        {
            var item = new MenuItem { Header = text, IsEnabled = Page.SelectedIds.Count >= 3 };
            item.Click += async (_, _) => await Arrange("distribute", axis);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private Task Arrange(string op, string value)
    {
        if (shownFigure is not { } figure || Page.SelectedIds.Count < (op == "align" ? 2 : 3) ||
            !Page.Selections.All(FigureCanvas.Movable)) return Task.CompletedTask;
        var oids = Page.SelectedIds;
        object args = op == "align" ? new { figure, op, oids, how = value } : new { figure, op, oids, axis = value };
        return EditFigure(figure, args, "Arranging layers…");
    }

    private void ViewportResized(object sender, SizeChangedEventArgs e) => UpdateZoom();
    private void ViewportWheel(object sender, MouseWheelEventArgs e)
    {
        if (!validFigure || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ChangeZoom(e.Delta > 0 ? "in" : "out");
        e.Handled = true;
    }
    private void ZoomClicked(object sender, RoutedEventArgs e) => ChangeZoom((string)((Button)sender).Tag);

    private void PanStarted(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !validFigure || Page.IsDragging || !Viewport.CaptureMouse()) return;
        pan = (e.GetPosition(Viewport), Viewport.HorizontalOffset, Viewport.VerticalOffset);
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
        if (e.ChangedButton != MouseButton.Middle || pan is null) return;
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
        zoom = command switch { "in" => Math.Min(8, zoom * 1.35), "out" => Math.Max(0.4, zoom / 1.35), _ => 1 };
        UpdateZoom();
        Viewport.UpdateLayout();
        Viewport.ScrollToHorizontalOffset(centerX * Viewport.ExtentWidth - Viewport.ViewportWidth / 2);
        Viewport.ScrollToVerticalOffset(centerY * Viewport.ExtentHeight - Viewport.ViewportHeight / 2);
    }

    private void UpdateZoom()
    {
        if (!validFigure || currentRender?.Size is not [var w, var h] || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
        double scale = zoom * Math.Min(Math.Max(1, Viewport.ActualWidth - 30) / w, Math.Max(1, Viewport.ActualHeight - 30) / h);
        PageView.Width = w * scale;
        PageView.Height = h * scale;
        ZoomText.Text = zoom == 1 ? "Fit" : $"{zoom:P0} of fit";
    }
}
