using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fignimble.Windows;

public sealed class FigureCanvas : FrameworkElement
{
    // The viewbox fits this surface to the window. All geometry and pointer deltas here are in millimeters.
    private readonly Dictionary<string, BitmapSource> images = new(StringComparer.OrdinalIgnoreCase);
    private FigureRender? figure;
    private string? selected;
    private Point? start;
    private Vector delta;
    private bool resizing;
    private const double HandleSize = 2.2;
    private static readonly Brush Accent = Brushes.DodgerBlue;
    private static readonly Pen SelectionPen = new(Accent, 0.45);

    internal event Action<CanvasEdit>? EditCompleted;
    internal bool IsDragging => start.HasValue;
    private FigureElement? Selection => figure?.Elements?.FirstOrDefault(e => e.Oid == selected);

    internal void ShowFigure(FigureRender? render, bool keepSelection)
    {
        if (!keepSelection) selected = null;
        figure = render;
        if (Selection is not { } selection || !Editable(selection)) selected = null;
        images.Clear();
        Width = render?.Size?[0] ?? 1;
        Height = render?.Size?[1] ?? 1;
        InvalidateVisual();
    }

    private static bool Editable(FigureElement e) => (e.Kind is "panel" or "image") && e.Parent is null && e.Host is null
        && e.Box is { Length: 4 } && e.Box[2] > 0 && e.Box[3] > 0;
    private static Rect Box(FigureElement e) => new(e.Box[0], e.Box[1], e.Box[2], e.Box[3]);
    private static Rect Handle(Rect r) => new(r.Right - HandleSize, r.Bottom - HandleSize, HandleSize, HandleSize);

    protected override void OnRender(DrawingContext dc)
    {
        if (figure is null) return;
        var page = new Rect(0, 0, Width, Height);
        Brush bg = Brushes.White;
        if (figure.Background is { } color)
            try { bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
            catch (FormatException) { }
        dc.PushClip(new RectangleGeometry(page));
        dc.DrawRectangle(bg, null, page);
        foreach (var e in figure.Elements ?? [])
        {
            Vector shift = IsDragging && !resizing && e.Oid == selected ? delta : default;
            DrawSprite(dc, e.Sprite, shift);
        }
        foreach (var label in figure.Labels ?? []) DrawSprite(dc, label.Sprite, default);
        if (Selection is { } active)
        {
            Rect selectionBox = DragBox(active);
            dc.DrawRectangle(null, SelectionPen, selectionBox);
            dc.DrawRectangle(Accent, null, Handle(selectionBox));
        }
        dc.Pop();
    }

    private void DrawSprite(DrawingContext dc, Sprite? sprite, Vector shift)
    {
        if (sprite is null || sprite.W <= 0 || sprite.H <= 0) return;
        if (!images.TryGetValue(sprite.File, out var bitmap))
        {
            try
            {
                using var file = File.Open(sprite.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var loaded = new BitmapImage();
                loaded.BeginInit();
                loaded.CacheOption = BitmapCacheOption.OnLoad;
                loaded.StreamSource = file;
                loaded.EndInit();
                loaded.Freeze();
                images[sprite.File] = bitmap = loaded;
            }
            catch (IOException) { return; }
            catch (NotSupportedException) { return; }
        }
        dc.DrawImage(bitmap, new Rect(sprite.X + shift.X, sprite.Y + shift.Y, sprite.W, sprite.H));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Point p = e.GetPosition(this);
        resizing = Selection is { } current && Handle(Box(current)).Contains(p);
        if (!resizing)
            selected = figure?.Elements?.LastOrDefault(el => Editable(el) && Box(el).Contains(p))?.Oid;
        if (selected is not null && CaptureMouse()) { start = p; delta = default; }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point p = e.GetPosition(this);
        if (start is { } from) { delta = p - from; InvalidateVisual(); }
        Cursor = (IsDragging && resizing) || (Selection is { } active && Handle(Box(active)).Contains(p))
            ? Cursors.SizeNWSE : IsDragging || (figure?.Elements?.Any(el => Editable(el) && Box(el).Contains(p)) ?? false)
                ? Cursors.SizeAll : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        CanvasEdit? edit = null;
        if (start is { } from && Selection is { } active)
        {
            delta = e.GetPosition(this) - from;
            if (Math.Abs(delta.X) >= 0.15 || Math.Abs(delta.Y) >= 0.15)
                edit = new(active.Oid, resizing ? DragBox(active) : null, delta);
        }
        start = null;
        ReleaseMouseCapture();
        InvalidateVisual();
        if (edit is not null) EditCompleted?.Invoke(edit);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        start = null;
        InvalidateVisual();
    }

    private Rect DragBox(FigureElement active)
    {
        Rect b = Box(active);
        if (!IsDragging) return b;
        if (!resizing) { b.Offset(delta); return b; }
        return new Rect(b.Location, new Size(Math.Max(1, b.Width + delta.X), Math.Max(1, b.Height + delta.Y)));
    }
}

internal sealed record CanvasEdit(string Oid, Rect? NewBox, Vector Delta);
