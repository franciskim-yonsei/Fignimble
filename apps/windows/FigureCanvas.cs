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
    private string[] selected = [];
    private Point? start;
    private Vector delta;
    private bool resizing;
    private const double HandleSize = 2.2;
    private static readonly Brush Accent = Brushes.DodgerBlue;
    private static readonly Pen SelectionPen = new(Accent, 0.45);

    internal event Action<CanvasEdit>? EditCompleted;
    internal event Action? SelectionChanged;
    internal bool IsDragging => start.HasValue;
    internal IReadOnlyList<string> SelectedIds => selected;
    internal FigureElement[] Selections => (figure?.Elements ?? []).Where(e => selected.Contains(e.Oid)).ToArray();
    internal FigureElement? Selection => selected.Length == 1 ? Selections.FirstOrDefault() : null;

    internal void Select(string? oid) => SelectIds(oid is null ? [] : [oid]);

    internal void SelectIds(IEnumerable<string> ids)
    {
        var available = (figure?.Elements ?? []).Where(TopLevel).Select(e => e.Oid).ToHashSet();
        var next = ids.Where(available.Contains).Distinct().ToArray();
        if (selected.SequenceEqual(next)) return;
        selected = next;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    internal void ShowFigure(FigureRender? render, bool keepSelection)
    {
        figure = render;
        SelectIds(keepSelection ? selected : []);
        images.Clear();
        Width = render?.Size?[0] ?? 1;
        Height = render?.Size?[1] ?? 1;
        InvalidateVisual();
    }

    internal static bool TopLevel(FigureElement e) => e.Parent is null;
    internal static bool Unlocked(FigureElement e) => e.Props is not { Locked: true } and not { Hidden: true };
    internal static bool Movable(FigureElement e) => TopLevel(e) && Unlocked(e) && e.Host is null && HasBox(e) &&
        e.Kind is "panel" or "image" or "text" or "line" or "bracket" or "rect" or "ellipse" or "figure" or "table" or "layout" or "qr" or "path" or "legend";
    internal static bool Resizable(FigureElement e) => Movable(e) && e.Kind is "panel" or "image";
    private static bool CanHit(FigureElement e) => TopLevel(e) && e.Props is not { Hidden: true } && HasBox(e);
    internal static bool HasBox(FigureElement e) => e.Box is { Length: 4 } b && b[2] >= 0 && b[3] >= 0;
    internal static Rect Box(FigureElement e) => new(e.Box![0], e.Box[1], e.Box[2], e.Box[3]);
    private static Rect HitBox(FigureElement e) { var b = Box(e); b.Inflate(0.9, 0.9); return b; }
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
            bool moving = IsDragging && !resizing && (selected.Contains(e.Oid) || e.Parent is { } id && selected.Contains(id));
            Vector shift = moving ? delta : default;
            DrawSprite(dc, e.Sprite, shift);
        }
        foreach (var label in figure.Labels ?? [])
            DrawSprite(dc, label.Sprite, IsDragging && !resizing && label.Panel is { } id && selected.Contains(id) ? delta : default);
        foreach (var active in Selections.Where(CanHit))
        {
            Rect selectionBox = DragBox(active);
            dc.DrawRectangle(null, SelectionPen, selectionBox);
            if (selected.Length == 1 && Resizable(active)) dc.DrawRectangle(Accent, null, Handle(selectionBox));
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
        resizing = Selection is { } current && Resizable(current) && Handle(Box(current)).Contains(p);
        if (!resizing)
        {
            var hit = figure?.Elements?.LastOrDefault(el => CanHit(el) && el.Props is not { Locked: true } && HitBox(el).Contains(p));
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
            {
                if (hit is not null) SelectIds(selected.Contains(hit.Oid) ? selected.Where(id => id != hit.Oid) : selected.Append(hit.Oid));
                e.Handled = true;
                return;
            }
            if (hit is null || !selected.Contains(hit.Oid)) Select(hit?.Oid);
        }
        if (selected.Length > 0 && Selections.All(Movable) && CaptureMouse()) { start = p; delta = default; }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point p = e.GetPosition(this);
        if (start is { } from) { delta = p - from; InvalidateVisual(); }
        Cursor = (IsDragging && resizing) || (Selection is { } active && Resizable(active) && Handle(Box(active)).Contains(p))
            ? Cursors.SizeNWSE : IsDragging || (figure?.Elements?.Any(el => Movable(el) && HitBox(el).Contains(p)) ?? false)
                ? Cursors.SizeAll : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        CanvasEdit? edit = null;
        if (start is { } from)
        {
            delta = e.GetPosition(this) - from;
            if (Math.Abs(delta.X) >= 0.15 || Math.Abs(delta.Y) >= 0.15)
                edit = new(selected, resizing && Selection is { } active ? DragBox(active) : null, delta);
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

internal sealed record CanvasEdit(string[] Oids, Rect? NewBox, Vector Delta);
