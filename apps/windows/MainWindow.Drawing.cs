using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private sealed record ToolInfo(CanvasTool Tool, Key Key, string Label, string Glyph);
    private static readonly ToolInfo[] DrawingTools =
    [
        new(CanvasTool.Select, Key.V, "Select", "↖"), new(CanvasTool.Text, Key.T, "Text", "T"),
        new(CanvasTool.Line, Key.L, "Line", "╱"), new(CanvasTool.Arrow, Key.A, "Arrow", "↗"),
        new(CanvasTool.Rect, Key.R, "Rectangle", "▭"), new(CanvasTool.Ellipse, Key.E, "Ellipse", "○"),
        new(CanvasTool.Bracket, Key.B, "Significance bracket", "⌜⌝"), new(CanvasTool.Pen, Key.P, "Pen", "✎"),
        new(CanvasTool.Zoom, Key.Z, "Zoom to area", "⌕")
    ];
    private TextBox? inlineText;

    private void SetupDrawing()
    {
        foreach (var tool in DrawingTools)
        {
            var button = new Button { Style = (Style)FindResource("ToolButton"), Tag = tool.Tool,
                Content = tool.Glyph, ToolTip = $"{tool.Label} ({tool.Key})", Width = 25, Padding = new Thickness(2),
                FontSize = 15, FontFamily = new FontFamily("Segoe UI Symbol") };
            AutomationProperties.SetName(button, tool.Label);
            button.Click += (_, _) => PickTool(tool.Tool);
            CanvasDrawingTools.Children.Add(button);
        }
        Page.ToolChanged += UpdateDrawingTools;
        Page.ShapeDrawn += async (kind, props) => await InsertShape(kind, props);
        Page.HandleEdited += async (op, props) =>
        {
            if (!validFigure || shownFigure is not { } figure) return;
            var args = new Dictionary<string, object?> { ["figure"] = figure, ["op"] = op };
            foreach (var field in System.Text.Json.JsonSerializer.SerializeToElement(props).EnumerateObject()) args[field.Name] = field.Value;
            await EditFigure(figure, args, "Updating handle…");
        };
        Page.TextRequested += (point, element) => OpenInlineText(point, element);
        Page.TableCellRequested += OpenInlineCell;
        Page.ElementOpened += async el =>
        {
            if (el.Kind == "figure" && el.Props?.Source is { } path &&
                figureCards.FirstOrDefault(f => f.Name == System.IO.Path.GetFileNameWithoutExtension(path)) is { } card)
                Figures.SelectedItem = card;
            else if (el.Kind == "label") await ToggleLabelLink();
            else if (el.Kind == "image") StartPictureCrop();
            else if (el.Kind is "qr" or "bracket") await EditDetails();
            else await EditSource();
        };
        Page.ZoomRequested += ZoomToArea;
        Page.PointerMoved += p => PointerReadout.Text = p is { } point && validFigure
            ? $"{point.X:N1}, {point.Y:N1} mm" : "";
        UpdateDrawingTools(Page.Tool);
    }

    private void PickTool(CanvasTool tool)
    {
        Page.CancelCrop();
        Page.SetTool(tool);
        Page.Focus();
    }

    private void UpdateDrawingTools(CanvasTool active)
    {
        foreach (var button in CanvasDrawingTools.Children.OfType<Button>())
        {
            bool on = button.Tag is CanvasTool tool && tool == active;
            button.Background = on ? (Brush)FindResource("WorkspaceSelection") : Brushes.Transparent;
            AutomationProperties.SetItemStatus(button, on ? "Selected" : "");
        }
        if (active != CanvasTool.Select)
            Status.Text = active switch
            {
                CanvasTool.Pen => "Pen: click points, then press Enter or double-click to finish. Click the first point to close.",
                CanvasTool.Text => "Text: click the figure to type. Enter applies; Esc cancels.",
                CanvasTool.Zoom => "Zoom: drag a box, click to zoom in, or Alt+click to zoom out.",
                _ => "Drag to draw. Shift constrains lines and shapes; Alt keeps the tool active."
            };
    }

    private bool DrawingShortcut(Key key, ModifierKeys mods)
    {
        if (!Page.IsKeyboardFocusWithin || mods != ModifierKeys.None || !validFigure || Page.IsDragging) return false;
        if (key == Key.Enter && Page.Tool == CanvasTool.Pen) { Page.FinishPen(false); return true; }
        if (DrawingTools.FirstOrDefault(t => t.Key == key) is not { } tool) return false;
        PickTool(tool.Tool);
        return true;
    }

    private Task InsertShape(string kind, object props) => shownFigure is not { } figure || !validFigure
        ? Task.CompletedTask : EditFigure(figure, new { figure, op = "insert", kind, props }, "Adding " + kind + "…");

    private void OpenInlineText(Point point, FigureElement? element) => OpenCanvasEditor(point, element, null);

    private void OpenCanvasEditor(Point point, FigureElement? element, PanelText? plotText)
    {
        if (busy || !validFigure || shownFigure is not { } figure) return;
        if (inlineText is not null) return;
        string original = plotText?.Text ?? element?.Props?.Text ?? element?.Text ?? "";
        double size = plotText?.Size ?? element?.Props?.Size ?? 8;
        var expectedRender = currentRender;
        if (!double.IsFinite(size)) size = 8;
        double x = Math.Round(point.X, 2), y = Math.Round(point.Y, 2);
        var editor = new TextBox { Text = original, FontSize = Math.Clamp(size / 72 * 25.4, 2, 12),
            MinWidth = Math.Min(20, Page.Width), MaxWidth = Math.Max(20, Page.Width / 2),
            BorderThickness = new Thickness(0.35), Padding = new Thickness(1, 0.5, 1, 0.5),
            BorderBrush = (Brush)FindResource("WorkspaceAccent"), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
        AutomationProperties.SetName(editor, "Canvas text");
        Canvas.SetLeft(editor, Math.Clamp(x, 0, Math.Max(0, Page.Width - editor.MinWidth)));
        Canvas.SetTop(editor, Math.Clamp(y, 0, Math.Max(0, Page.Height - editor.FontSize * 2)));
        inlineText = editor;
        CanvasEditors.Children.Add(editor);
        async Task End(bool save, bool focus)
        {
            if (inlineText != editor) return;
            inlineText = null;
            CanvasEditors.Children.Remove(editor);
            if (focus) Page.Focus();
            string text = NormalizeSource(editor.Text);
            if (!save || figure != shownFigure || text == original || (plotText is null && string.IsNullOrWhiteSpace(text))) return;
            if (plotText is not null && element is not null)
            {
                if (StillEditing(expectedRender)) await ApplyPlotText(figure, element.Oid, plotText.Key, plotText.Text, text);
            }
            else if (element is null) await InsertShape("text", new { x, y, text });
            else await SetDetails(figure, element.Oid, new() { ["text"] = text });
        }
        editor.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Escape && (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) return;
            e.Handled = true;
            await End(e.Key == Key.Enter, true);
        };
        editor.LostKeyboardFocus += async (_, _) => await End(true, false);
        editor.Focus();
        editor.SelectAll();
    }

    private void ZoomToArea(Rect area, bool outward)
    {
        if (!validFigure || Page.Width <= 0 || Page.Height <= 0 || PageView.ActualWidth <= 0) return;
        double scale = PageView.ActualWidth / Page.Width;
        bool click = area.Width * scale < 6 || area.Height * scale < 6;
        double ratio = click ? (outward ? 1 / 1.35 : 1.35) : Math.Min(
            Math.Max(1, Viewport.ViewportWidth - 18) / (area.Width * scale),
            Math.Max(1, Viewport.ViewportHeight - 18) / (area.Height * scale));
        ZoomAround(new Point(area.X + area.Width / 2, area.Y + area.Height / 2), Math.Clamp(zoom * ratio, 0.4, 16),
            new Point(Viewport.ViewportWidth / 2, Viewport.ViewportHeight / 2));
    }

    private void ZoomAround(Point anchor, double next, Point viewportPoint)
    {
        zoom = next;
        UpdateZoom();
        Viewport.UpdateLayout();
        if (PageView.ActualWidth <= 0 || PageView.ActualHeight <= 0) return;
        Point actual = Page.TranslatePoint(anchor, Viewport);
        Viewport.ScrollToHorizontalOffset(Viewport.HorizontalOffset + actual.X - viewportPoint.X);
        Viewport.ScrollToVerticalOffset(Viewport.VerticalOffset + actual.Y - viewportPoint.Y);
    }
}
