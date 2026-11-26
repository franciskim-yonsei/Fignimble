using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fignimble.Windows;

public sealed partial class FigureCanvas : FrameworkElement
{
    // The viewbox fits this surface to the window. All geometry and pointer deltas here are in millimeters.
    private readonly Dictionary<string, BitmapSource> images = new(StringComparer.OrdinalIgnoreCase);
    private FigureRender? figure;
    internal int VisionMode { get; private set; } = -1;
    internal void SetVision(int mode)
    {
        if (VisionMode == mode) return;
        VisionMode = mode;
        images.Clear();
        InvalidateVisual();
    }
    private string[] selected = [];
    private Point? start;
    private Vector delta;
    private bool resizing => resizeGrip.HasValue;
    private (string Oid, int Index, LayoutDivider Divider)? dividerDrag;
    private bool marquee;
    private string[] marqueeBase = [];
    private double? snapX, snapY;
    private FigureGuide? previewGuide;
    internal void PreviewGuide(FigureGuide? guide) { previewGuide = guide; InvalidateVisual(); }
    internal bool SnapEnabled { get; set; } = true;
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(36, 107, 107));
    private static readonly Pen SelectionPen = new(Accent, 0.45);

    internal event Action<CanvasEdit>? EditCompleted;
    internal event Action<string, int, double>? DividerEdited;
    internal event Action<FigureElement, PanelText>? PlotTextClicked;
    internal event Action? SelectionChanged;
    internal (string Oid, string Key, bool Text)? PanelPart { get; private set; }

    internal bool ClearPanelPart()
    {
        if (PanelPart is null) return false;
        PanelPart = null;
        InvalidateVisual();
        SelectionChanged?.Invoke();
        return true;
    }

    internal void SelectPanelPart(FigureElement layer, string key, bool text)
    {
        Select(layer.Oid);
        var next = (layer.Oid, key, text);
        if (PanelPart == next) return;
        PanelPart = next;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }
    internal event Action<FigureElement, int, int>? TableCellRequested;
    internal bool IsDragging => start.HasValue || IsDrawingTool;
    internal IReadOnlyList<string> SelectedIds => selected;
    internal FigureElement[] Selections => (figure?.Elements ?? []).Concat(figure?.Labels ?? []).Where(e => selected.Contains(e.Oid)).ToArray();
    internal FigureElement? Selection => selected.Length == 1 ? Selections.FirstOrDefault() : null;

    internal void Select(string? oid) => SelectIds(oid is null ? [] : [oid]);
    internal void SelectAll() => SelectIds((figure?.Elements ?? []).Where(e => TopLevel(e) && Unlocked(e)).Select(e => e.Oid));
    private FigureElement? HitLabel(Point p) => figure?.Labels?.LastOrDefault(l => HasBox(l) && HitBox(l).Contains(p));
    private FigureElement? HitElement(Point p) => figure?.Elements?.LastOrDefault(el => Unlocked(el) && HasBox(el) && HitBox(el).Contains(p) &&
        (el.Parent is null || el.Kind is "panel" or "image"));

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        var hit = HitLabel(p) ?? HitElement(p);
        if (hit is null) Select(null);
        else if (!selected.Contains(hit.Oid)) SelectIds(hit.Kind == "label" ? [hit.Oid] : CanvasGroup(hit));
        base.OnMouseRightButtonDown(e);
    }

    internal string[] IncludeGroupMembers(IEnumerable<FigureElement> layers)
    {
        var items = layers.ToArray();
        var ids = items.Select(e => e.Oid).ToHashSet();
        var groups = items.Select(e => e.Group).OfType<string>().ToHashSet();
        return (figure?.Elements ?? []).Where(e => TopLevel(e) &&
            (ids.Contains(e.Oid) || e.Group is { } g && groups.Contains(g))).Select(e => e.Oid).ToArray();
    }

    private string[] CanvasGroup(FigureElement layer) => layer.Parent is not null ? [layer.Oid] : IncludeGroupMembers([layer])
        .Where(id => (figure?.Elements ?? []).Any(e => e.Oid == id && Unlocked(e)))
        .OrderBy(id => id == layer.Oid ? 0 : 1).ToArray();

    internal void SelectIds(IEnumerable<string> ids)
    {
        var available = (figure?.Elements ?? []).Concat(figure?.Labels ?? []).Select(e => e.Oid).ToHashSet();
        var next = ids.Where(available.Contains).Distinct().ToArray();
        if (selected.SequenceEqual(next)) return;
        selected = next;
        if (!Cropping) CancelCrop();
        if (PanelPart is { } part && (next.Length != 1 || next[0] != part.Oid)) PanelPart = null;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    internal void ShowFigure(FigureRender? render, bool keepSelection)
    {
        if (!keepSelection) SetTool(CanvasTool.Select);
        figure = render;
        SelectIds(keepSelection ? selected : []);
        images.Clear();
        Width = render?.Size?[0] ?? 1;
        Height = render?.Size?[1] ?? 1;
        InvalidateVisual();
    }

    internal static bool TopLevel(FigureElement e) => e.Parent is null;
    internal static bool Unlocked(FigureElement e) => e.Props is not { Locked: true } and not { Hidden: true };
    private bool moveAllLabels;
    internal static bool CanMove(FigureElement e) => Movable(e) || HasBox(e) && Unlocked(e) &&
        (e.Kind == "label" || e.Parent is not null && e.Kind is "panel" or "image");

    internal bool CanMoveSelection() => Selections.All(e => CanMove(e) && (e.Parent is not { } parent ||
        figure?.Elements?.FirstOrDefault(l => l.Oid == parent) is { } layout && Movable(layout)));
    internal static bool Movable(FigureElement e) => TopLevel(e) && Unlocked(e) && HasBox(e) &&
        e.Kind is "panel" or "image" or "inset" or "text" or "line" or "bracket" or "rect" or "ellipse" or "figure" or "table" or "layout" or "qr" or "path" or "legend" or "zoom" or "scalebar";
    internal static bool Resizable(FigureElement e) => Movable(e) && e.Kind is "panel" or "image" or "layout" or "inset" or "rect" or "ellipse" or "figure" or "qr" or "zoom" or "path";
    private static bool CanHit(FigureElement e) => TopLevel(e) && e.Props is not { Hidden: true } && HasBox(e);
    internal static bool HasBox(FigureElement e) => e.Box is { Length: 4 } b && b[2] >= 0 && b[3] >= 0;
    internal static Rect Box(FigureElement e) => new(e.Box![0], e.Box[1], e.Box[2], e.Box[3]);
    private static Rect HitBox(FigureElement e) { var b = Box(e); b.Inflate(0.9, 0.9); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        if (figure is null) return;
        var page = new Rect(0, 0, Width, Height);
        Brush bg = Brushes.White;
        if (figure.Background is { } color)
            try { bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
            catch (FormatException) { }
        dc.PushClip(new RectangleGeometry(page));
        if (figure.Background is null) bg = TransparencyBrush;
        dc.DrawRectangle(bg, null, page);
        DrawGrid(dc);
        var moved = start.HasValue && !marquee && !resizing && endpoint is null && pathPoint is null && canvasGuide is null && dividerDrag is null
            ? Selections.Select(e => e.Parent ?? e.Oid).ToHashSet() : [];
        foreach (var e in figure.Elements ?? [])
        {
            bool moving = moved.Contains(e.Oid) || e.Parent is { } id && moved.Contains(id);
            Vector shift = moving ? delta : default;
            DrawSprite(dc, e.Sprite, shift);
        }
        foreach (var label in figure.Labels ?? [])
        {
            bool moves = moveAllLabels && !label.Detached || moved.Contains(label.Oid) || !label.Detached && label.Panel is { } id &&
                (moved.Contains(id) || figure.Elements?.FirstOrDefault(l => l.Oid == id)?.Parent is { } parentLayout && moved.Contains(parentLayout));
            DrawSprite(dc, label.Sprite, start.HasValue && moves && !marquee && !resizing && endpoint is null && canvasGuide is null && dividerDrag is null ? delta : default);
        }
        foreach (var active in Selections.Where(e => HasBox(e) && e.Props is not { Hidden: true }))
        {
            Rect selectionBox = DragBox(active);
            if (Cropping && Selection == active) DrawCropMask(dc, Box(active), selectionBox);
            dc.DrawRectangle(null, SelectionPen, selectionBox);
            if (selected.Length == 1) DrawGrips(dc, active, selectionBox);
        }
        if (Selection is { Kind: "layout", Dividers: { } dividers } layout && Unlocked(layout))
            foreach (var (d, index) in dividers.Select((d, index) => (d, index)).Where(t => t.d.Editable))
            {
                double shift = dividerDrag?.Index == index ? (d.Axis == "col" ? delta.X : delta.Y) : 0;
                double at = d.At + shift;
                if (d.Axis == "col") dc.DrawLine(SelectionPen, new Point(at, d.From), new Point(at, d.To));
                else dc.DrawLine(SelectionPen, new Point(d.From, at), new Point(d.To, at));
            }
        var guidePen = new Pen(Brushes.Teal, 0.24) { DashStyle = DashStyles.Dash };
        void DrawGuide(FigureGuide guide)
        {
            if (guide.X is { } x) dc.DrawLine(guidePen, new Point(x, 0), new Point(x, Height));
            if (guide.Y is { } y) dc.DrawLine(guidePen, new Point(0, y), new Point(Width, y));
        }
        if (ShowGuides && figure.Guides is { } guides)
            for (int i = 0; i < guides.Length; i++)
            {
                if (canvasGuide is { } moving && moving.Index == i)
                    DrawGuide(moving.Vertical ? new FigureGuide(guidePoint.X, null) : new FigureGuide(null, guidePoint.Y));
                else DrawGuide(guides[i]);
            }
        if (previewGuide is { } preview) DrawGuide(preview);
        if (snapX is { } sx) dc.DrawLine(SelectionPen, new Point(sx, 0), new Point(sx, Height));
        if (snapY is { } sy) dc.DrawLine(SelectionPen, new Point(0, sy), new Point(Width, sy));
        DrawGapHints(dc);
        if (marquee && start is { } anchor)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 30, 144, 255)), SelectionPen, new Rect(anchor, anchor + delta));
        if (PanelPart is { } part && Selection is { } panel)
        {
            var bounds = part.Text ? panel.Texts?.FirstOrDefault(t => t.Key == part.Key)?.Box : panel.Marks?.FirstOrDefault(m => m.Key == part.Key)?.Box;
            if (bounds is [var x, var y, >= 0 and var w, >= 0 and var h])
            {
                var box = new Rect(x, y, w, h);
                box.Inflate(0.8, 0.8);
                dc.DrawRectangle(null, SelectionPen, box);
            }
        }
        DrawToolPreview(dc);
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
                images[sprite.File] = bitmap = VisionMode < 0 ? loaded : MainWindow.SimulateVision(loaded, VisionMode);
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
        if (ToolDown(e, p)) { e.Handled = true; return; }
        bool adding = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        if (!adding && HitLabel(p) is { } label)
        {
            Select(label.Oid);
            bool individual = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            if (e.ClickCount == 2)
            {
                if (individual) TextRequested?.Invoke(Box(label).TopLeft, label);
                else ElementOpened?.Invoke(label);
            }
            else if (CaptureMouse()) { start = p; delta = default; moveAllLabels = !individual && !label.Detached; }
            e.Handled = true;
            return;
        }
        if (!adding && e.ClickCount == 2)
        {
            if (PlotTextClicked is not null && HitPlotText(p)) { e.Handled = true; return; }
            if (HitElement(p) is { Kind: "table" } table && HitCell(table, p) is { } cell)
            {
                Select(table.Oid);
                TableCellRequested?.Invoke(table, cell.Row, cell.Column);
                e.Handled = true;
                return;
            }
            if (HitElement(p) is { } layer && layer.Kind is "text" or "panel" or "inset" or "figure" or "bracket" or "qr" or "image")
            {
                Select(layer.Oid);
                if (layer.Kind == "text" && layer.Code?.File is null) TextRequested?.Invoke(new Point(layer.Box![0], layer.Box[1]), layer);
                else ElementOpened?.Invoke(layer);
                e.Handled = true;
                return;
            }
        }
        if (!adding && BeginPathPoint(p)) { e.Handled = true; return; }
        if (!adding && BeginEndpoint(p)) { e.Handled = true; return; }
        if (!adding && HitDivider(p) is { } divider && CaptureMouse())
        {
            dividerDrag = divider;
            start = p;
            delta = default;
            e.Handled = true;
            return;
        }
        resizeGrip = !adding && Selection is { } current && HasBox(current) ? HitGrip(current, p) : null;
        if (!resizing)
        {
            if (Cropping) CancelCrop();
            var hit = HitElement(p);
            if (hit is null)
            {
                if (!adding && StartCanvasGuide(p)) { e.Handled = true; return; }
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
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || !PickPanelPart(p)) ClearPanelPart();
        }
        if (selected.Length > 0 && CanMoveSelection() && CaptureMouse()) { start = p; delta = default; }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point p = e.GetPosition(this);
        PointerMoved?.Invoke(p);
        if (ToolMove(p)) return;
        if ((canvasGuide ?? HitCanvasGuide(p)) is { } guide)
        {
            if (canvasGuide is not null) { guidePoint = p; InvalidateVisual(); }
            if (canvasGuide is not null || !start.HasValue) { Cursor = guide.Vertical ? Cursors.SizeWE : Cursors.SizeNS; return; }
        }
        if (start is { } from) { UpdateDrag(from, p); InvalidateVisual(); }
        if ((dividerDrag ?? HitDivider(p)) is { } divider)
        {
            Cursor = divider.Divider.Axis == "col" ? Cursors.SizeWE : Cursors.SizeNS;
            return;
        }
        if (Selection is { } active && HasBox(active) && (resizeGrip ?? HitGrip(active, p)) is { } grip)
            Cursor = ResizeCursor(grip);
        else if (endpoint.HasValue || Selection is { } line && LineEnds(line) is { } ends &&
                 (GripBounds(ends.Start).Contains(p) || GripBounds(ends.End).Contains(p))) Cursor = Cursors.Cross;
        else Cursor = IsDragging || HitElement(p) is not null ? Cursors.SizeAll : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Point release = e.GetPosition(this);
        if (ToolUp(release) || FinishCanvasGuide(release)) { e.Handled = true; return; }
        CanvasEdit? edit = null;
        var movedDivider = dividerDrag;
        (string Op, object Props)? handleEdit = null;
        if (start is { } from)
        {
            UpdateDrag(from, e.GetPosition(this));
            if (!marquee && movedDivider is null && delta.Length >= 0.15)
            {
                if (pathPoint is { } vertex)
                {
                    Point at = SnapToolPoint(vertex.Points[vertex.Index] + delta);
                    var points = vertex.Points.Select((p, i) => i == vertex.Index ? new[] { Mm(at.X), Mm(at.Y) } : new[] { p.X, p.Y }).ToArray();
                    handleEdit = ("points", new { oid = vertex.Oid, points });
                }
                else if (endpoint is { } move)
                {
                    var p = DraggedEndpoint(move);
                    handleEdit = ("endpoints", move.First ? (object)new { oid = move.Oid, x1 = Mm(p.X), y1 = Mm(p.Y) }
                        : new { oid = move.Oid, x2 = Mm(p.X), y2 = Mm(p.Y) });
                }
                else if (Cropping && resizeGrip.HasValue && Selection is { } picture)
                {
                    var box = DragBox(picture);
                    handleEdit = ("crop", new { oid = picture.Oid, box = new[] { Mm(box.X), Mm(box.Y), Mm(box.Width), Mm(box.Height) } });
                }
                else if (Selection is { Kind: "text" } text && resizeGrip is { Y: 0 })
                {
                    var box = DragBox(text);
                    double x = text.Props?.Align switch { "center" => box.Left + box.Width / 2, "right" => box.Right, _ => box.Left };
                    handleEdit = ("set", new { oid = text.Oid, props = new { x = Mm(x), width = Mm(box.Width) } });
                }
                else edit = new(selected, resizing && Selection is { } active ? DragBox(active) : null, delta) { MoveAllLabels = moveAllLabels };
            }
        }
        ResetDrag();
        ReleaseMouseCapture();
        InvalidateVisual();
        if (edit is not null) EditCompleted?.Invoke(edit);
        if (handleEdit is { } handle) HandleEdited?.Invoke(handle.Op, handle.Props);
        if (movedDivider is { } divider)
        {
            double distance = divider.Divider.Axis == "col" ? delta.X : delta.Y;
            if (Math.Abs(distance) >= 0.15) DividerEdited?.Invoke(divider.Oid, divider.Index, distance);
        }
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        toolAnchor = toolEnd = null;
        ResetDrag();
        InvalidateVisual();
    }

    private void ResetDrag()
    {
        start = null;
        marquee = false;
        moveAllLabels = false;
        dividerDrag = null;
        canvasGuide = null;
        resizeGrip = null;
        endpoint = null;
        pathPoint = null;
        marqueeBase = [];
        snapX = snapY = null;
        xGaps = yGaps = [];
    }

    private void UpdateDrag(Point from, Point to)
    {
        delta = to - from;
        snapX = snapY = null;
        xGaps = yGaps = [];
        if (marquee)
        {
            if (delta.Length < 0.4) { SelectIds(marqueeBase); return; }
            var area = new Rect(from, to);
            var hits = (figure?.Elements ?? []).Where(e => CanHit(e) && Unlocked(e) && area.IntersectsWith(Box(e)));
            SelectIds(marqueeBase.Concat(hits.SelectMany(CanvasGroup)));
            return;
        }
        if (dividerDrag is not null || endpoint is not null || pathPoint is not null || (!SnapEnabled && !SnapGrid) || delta.Length < 0.15 || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        var moving = Selections;
        if (moving.Length == 0) return;
        var frameIds = moving.Select(e => e.Parent ?? e.Oid).ToHashSet();
        var frames = (figure?.Elements ?? []).Concat(figure?.Labels ?? []).Where(e => frameIds.Contains(e.Oid) && HasBox(e)).Select(Box).ToArray();
        if (frames.Length == 0) return;
        Rect union = frames[0];
        foreach (var frame in frames.Skip(1)) union.Union(frame);
        var targets = (figure?.Elements ?? []).Where(e => CanHit(e) && !frameIds.Contains(e.Oid)).Select(Box).ToArray();
        var guides = ShowGuides ? figure?.Guides ?? [] : [];
        IEnumerable<double> xs = SnapEnabled ? targets.SelectMany(b => Axis(b.Left, b.Right)).Concat(Axis(0, Width)).Concat(guides.Select(g => g.X).OfType<double>()) : [];
        IEnumerable<double> ys = SnapEnabled ? targets.SelectMany(b => Axis(b.Top, b.Bottom)).Concat(Axis(0, Height)).Concat(guides.Select(g => g.Y).OfType<double>()) : [];
        double tolerance = Math.Min(3, 6 / CanvasScale());
        double[] movingX = Axis(union.Left + delta.X, union.Right + delta.X), movingY = Axis(union.Top + delta.Y, union.Bottom + delta.Y);
        if (resizeGrip is { } grip && Selection is { } target)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || target.Kind is "image" or "figure" or "text" or "qr") return;
            movingX = grip.X == 0 ? [] : [grip.X < 0 ? union.Left + delta.X : union.Right + delta.X];
            movingY = grip.Y == 0 ? [] : [grip.Y < 0 ? union.Top + delta.Y : union.Bottom + delta.Y];
        }
        (double dx, snapX) = Snap(movingX, xs, tolerance);
        (double dy, snapY) = Snap(movingY, ys, tolerance);
        if (SnapEnabled && !resizing)
        {
            var at = union;
            at.Offset(delta);
            if (snapX is null) (dx, xGaps) = EqualGapSnap(at, targets, horizontal: true, tolerance);
            at.Offset(dx, 0);
            if (snapY is null) (dy, yGaps) = EqualGapSnap(at, targets, horizontal: false, tolerance);
        }
        if (SnapGrid && movingX.Length > 0 && snapX is null && xGaps.Length == 0) { snapX = Math.Round(movingX[0] / GridStep) * GridStep; dx = snapX.Value - movingX[0]; }
        if (SnapGrid && movingY.Length > 0 && snapY is null && yGaps.Length == 0) { snapY = Math.Round(movingY[0] / GridStep) * GridStep; dy = snapY.Value - movingY[0]; }
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

    private (string Oid, int Index, LayoutDivider Divider)? HitDivider(Point at)
    {
        if (Selection is not { Kind: "layout", Dividers: { } dividers } layout || !Unlocked(layout)) return null;
        for (int i = 0; i < dividers.Length; i++)
        {
            var d = dividers[i];
            double across = d.Axis == "col" ? at.X : at.Y, along = d.Axis == "col" ? at.Y : at.X;
            if (d.Editable && Math.Abs(across - d.At) < 1 && along >= d.From && along <= d.To) return (layout.Oid, i, d);
        }
        return null;
    }

    internal static bool CellBox(FigureElement table, int row, int column, out Rect cell)
    {
        cell = Rect.Empty;
        if (!HasBox(table) || table.Grid is not { } grid || row < 0 || column < 0 ||
            row >= grid.Rows.Length || column >= grid.Cols.Length || grid.Rows[row] is not [var y, > 0 and var h] ||
            grid.Cols[column] is not [var x, > 0 and var w]) return false;
        var box = Box(table);
        cell = new Rect(box.X + x, box.Y + y, w, h);
        return true;
    }

    private static (int Row, int Column)? HitCell(FigureElement table, Point point)
    {
        if (table.Grid is not { } g) return null;
        var b = Box(table);
        var p = point - b.TopLeft;
        int row = Array.FindIndex(g.Rows, r => r is [var y, var h] && p.Y >= y && p.Y < y + h);
        int col = Array.FindIndex(g.Cols, c => c is [var x, var w] && p.X >= x && p.X < x + w);
        return row >= 0 && col >= 0 ? (row, col) : null;
    }

    private bool HitPlotText(Point at)
    {
        foreach (var layer in (figure?.Elements ?? []).Reverse())
        {
            if (!Unlocked(layer) || !HasBox(layer) || !HitBox(layer).Contains(at)) continue;
            foreach (var text in layer.Texts ?? [])
            {
                if (text.Box is not [var x, var y, >= 0 and var w, >= 0 and var h]) continue;
                var bounds = new Rect(x, y, w, h);
                bounds.Inflate(0.5, 0.5);
                if (!bounds.Contains(at)) continue;
                SelectPanelPart(layer, text.Key, true);
                PlotTextClicked?.Invoke(layer, text);
                return true;
            }
            if (layer.Kind is not "layout" and not "legend") return false;
        }
        return false;
    }

    private bool PickPanelPart(Point p)
    {
        if (Selection is not { } layer || !Unlocked(layer)) return false;
        bool Inside(double[]? coordinates, double slop) => coordinates is [var x, var y, >= 0 and var w, >= 0 and var h] &&
            new Rect(x - slop, y - slop, w + 2 * slop, h + 2 * slop).Contains(p);
        if (layer.Texts?.FirstOrDefault(t => Inside(t.Box, 0.65)) is { } text)
        { SelectPanelPart(layer, text.Key, true); return true; }
        if (layer.Marks?.Where(m => Inside(m.Box, 0.5)).MinBy(m => m.Box![2] * m.Box[3]) is { } mark)
        { SelectPanelPart(layer, mark.Key, false); return true; }
        return false;
    }

    private Rect DragBox(FigureElement active)
    {
        Rect b = Box(active);
        if (!start.HasValue || marquee || dividerDrag is not null || endpoint is not null || pathPoint is not null || canvasGuide is not null) return b;
        if (resizeGrip is { } grip) return ResizeBox(active, b, grip);
        b.Offset(delta);
        return b;
    }
}

internal sealed record CanvasEdit(string[] Oids, Rect? NewBox, Vector Delta)
{
    public bool MoveAllLabels { get; init; }
}
