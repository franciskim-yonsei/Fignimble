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
    private bool marquee;
    private string[] marqueeBase = [];
    private double? snapX, snapY;
    internal bool SnapEnabled { get; set; } = true;
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

    internal string[] IncludeGroupMembers(IEnumerable<FigureElement> layers)
    {
        var items = layers.ToArray();
        var ids = items.Select(e => e.Oid).ToHashSet();
        var groups = items.Select(e => e.Group).OfType<string>().ToHashSet();
        return (figure?.Elements ?? []).Where(e => TopLevel(e) &&
            (ids.Contains(e.Oid) || e.Group is { } g && groups.Contains(g))).Select(e => e.Oid).ToArray();
    }

    private string[] CanvasGroup(FigureElement layer) => IncludeGroupMembers([layer])
        .Where(id => (figure?.Elements ?? []).Any(e => e.Oid == id && Unlocked(e))).ToArray();

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
            bool moving = IsDragging && !marquee && !resizing && (selected.Contains(e.Oid) || e.Parent is { } id && selected.Contains(id));
            Vector shift = moving ? delta : default;
            DrawSprite(dc, e.Sprite, shift);
        }
        foreach (var label in figure.Labels ?? [])
            DrawSprite(dc, label.Sprite, IsDragging && !marquee && !resizing && label.Panel is { } id && selected.Contains(id) ? delta : default);
        foreach (var active in Selections.Where(CanHit))
        {
            Rect selectionBox = DragBox(active);
            dc.DrawRectangle(null, SelectionPen, selectionBox);
            if (selected.Length == 1 && Resizable(active)) dc.DrawRectangle(Accent, null, Handle(selectionBox));
        }
        var guidePen = new Pen(Brushes.Teal, 0.24) { DashStyle = DashStyles.Dash };
        foreach (var guide in figure.Guides ?? [])
        {
            if (guide.X is { } x) dc.DrawLine(guidePen, new Point(x, 0), new Point(x, Height));
            if (guide.Y is { } y) dc.DrawLine(guidePen, new Point(0, y), new Point(Width, y));
        }
        if (snapX is { } sx) dc.DrawLine(SelectionPen, new Point(sx, 0), new Point(sx, Height));
        if (snapY is { } sy) dc.DrawLine(SelectionPen, new Point(0, sy), new Point(Width, sy));
        if (marquee && start is { } anchor)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 30, 144, 255)), SelectionPen, new Rect(anchor, anchor + delta));
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
        Focus();
        Point p = e.GetPosition(this);
        bool adding = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        resizing = !adding && Selection is { } current && Resizable(current) && Handle(Box(current)).Contains(p);
        if (!resizing)
        {
            var hit = figure?.Elements?.LastOrDefault(el => CanHit(el) && Unlocked(el) && HitBox(el).Contains(p));
            if (hit is null)
            {
                if (!adding) SelectIds([]);
                marqueeBase = selected;
                if (CaptureMouse()) { marquee = true; start = p; delta = default; }
                e.Handled = true;
                return;
            }
            var group = CanvasGroup(hit);
            if (adding)
            {
                SelectIds(selected.Contains(hit.Oid) ? selected.Except(group) : selected.Concat(group));
                e.Handled = true;
                return;
            }
            if (!selected.Contains(hit.Oid)) SelectIds(group);
        }
        if (selected.Length > 0 && Selections.All(Movable) && CaptureMouse()) { start = p; delta = default; }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point p = e.GetPosition(this);
        if (start is { } from) { UpdateDrag(from, p); InvalidateVisual(); }
        Cursor = (IsDragging && resizing) || (Selection is { } active && Resizable(active) && Handle(Box(active)).Contains(p))
            ? Cursors.SizeNWSE : IsDragging || (figure?.Elements?.Any(el => Movable(el) && HitBox(el).Contains(p)) ?? false)
                ? Cursors.SizeAll : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        CanvasEdit? edit = null;
        if (start is { } from)
        {
            UpdateDrag(from, e.GetPosition(this));
            if (!marquee && (Math.Abs(delta.X) >= 0.15 || Math.Abs(delta.Y) >= 0.15))
                edit = new(selected, resizing && Selection is { } active ? DragBox(active) : null, delta);
        }
        ResetDrag();
        ReleaseMouseCapture();
        InvalidateVisual();
        if (edit is not null) EditCompleted?.Invoke(edit);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        ResetDrag();
        InvalidateVisual();
    }

    private void ResetDrag()
    {
        start = null;
        marquee = false;
        marqueeBase = [];
        snapX = snapY = null;
    }

    private void UpdateDrag(Point from, Point to)
    {
        delta = to - from;
        snapX = snapY = null;
        if (marquee)
        {
            if (delta.Length < 0.4) { SelectIds(marqueeBase); return; }
            var area = new Rect(from, to);
            var hits = (figure?.Elements ?? []).Where(e => CanHit(e) && Unlocked(e) && area.IntersectsWith(Box(e)));
            SelectIds(marqueeBase.Concat(hits.SelectMany(CanvasGroup)));
            return;
        }
        if (resizing || !SnapEnabled || delta.Length < 0.15 || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        var moving = Selections;
        if (moving.Length == 0) return;
        Rect union = Box(moving[0]);
        foreach (var layer in moving.Skip(1)) union.Union(Box(layer));
        var targets = (figure?.Elements ?? []).Where(e => CanHit(e) && !selected.Contains(e.Oid)).Select(Box).ToArray();
        var guides = figure?.Guides ?? [];
        var xs = targets.SelectMany(b => Axis(b.Left, b.Right)).Concat(Axis(0, Width)).Concat(guides.Select(g => g.X).OfType<double>());
        var ys = targets.SelectMany(b => Axis(b.Top, b.Bottom)).Concat(Axis(0, Height)).Concat(guides.Select(g => g.Y).OfType<double>());
        double scale = PresentationSource.FromVisual(this) is null ? 1 : (PointToScreen(new Point(1, 0)) - PointToScreen(new Point())).Length;
        double tolerance = Math.Min(3, 6 / Math.Max(0.01, scale));
        (double dx, snapX) = Snap(Axis(union.Left + delta.X, union.Right + delta.X), xs, tolerance);
        (double dy, snapY) = Snap(Axis(union.Top + delta.Y, union.Bottom + delta.Y), ys, tolerance);
        delta += new Vector(dx, dy);
    }

    private static double[] Axis(double start, double end) => [start, (start + end) / 2, end];

    internal static (double Delta, double? Target) Snap(IEnumerable<double> source, IEnumerable<double> targets, double tolerance)
    {
        double best = tolerance, delta = 0;
        double? target = null;
        double[] origins = source.ToArray();
        foreach (double at in targets)
            foreach (double from in origins)
            {
                double diff = at - from;
                if (Math.Abs(diff) >= best) continue;
                best = Math.Abs(diff);
                delta = diff;
                target = at;
            }
        return (delta, target);
    }

    private Rect DragBox(FigureElement active)
    {
        Rect b = Box(active);
        if (!IsDragging || marquee) return b;
        if (!resizing) { b.Offset(delta); return b; }
        return new Rect(b.Location, new Size(Math.Max(1, b.Width + delta.X), Math.Max(1, b.Height + delta.Y)));
    }
}

internal sealed record CanvasEdit(string[] Oids, Rect? NewBox, Vector Delta);
