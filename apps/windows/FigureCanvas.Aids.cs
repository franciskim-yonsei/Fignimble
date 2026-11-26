using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Fignimble.Windows;

public sealed partial class FigureCanvas
{
    internal bool ShowGrid { get; set; }
    internal bool SnapGrid { get; set; }
    internal bool ShowGuides { get; set; } = true;
    internal double GridStep { get; set; } = 1;
    internal event Action<int, double?>? GuideEdited;
    private (int Index, bool Vertical)? canvasGuide;
    private Point guidePoint;
    private (Point A, Point B)[] xGaps = [], yGaps = [];
    internal event Action<Point?>? PointerMoved;
    private static readonly DrawingBrush TransparencyBrush = MakeTransparencyBrush();

    private static DrawingBrush MakeTransparencyBrush()
    {
        var checker = new DrawingGroup();
        using (var tile = checker.Open())
        {
            tile.DrawRectangle(Brushes.White, null, new Rect(0, 0, 4, 4));
            tile.DrawRectangle(Brushes.Gainsboro, null, new Rect(0, 0, 2, 2));
            tile.DrawRectangle(Brushes.Gainsboro, null, new Rect(2, 2, 2, 2));
        }
        var brush = new DrawingBrush(checker) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 4, 4) };
        brush.Freeze();
        return brush;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        PointerMoved?.Invoke(null);
    }

    private void DrawGapHints(DrawingContext dc)
    {
        foreach (var (a, b) in xGaps.Concat(yGaps))
        {
            dc.DrawLine(SelectionPen, a, b);
            var mark = Math.Abs(b.X - a.X) > 0 ? new Vector(0, 0.9) : new Vector(0.9, 0);
            dc.DrawLine(SelectionPen, a - mark, a + mark);
            dc.DrawLine(SelectionPen, b - mark, b + mark);
        }
    }

    internal static (double Shift, (Point A, Point B)[] Hints) EqualGapSnap(Rect moving, Rect[] targets, bool horizontal, double tolerance)
    {
        double From(Rect b) => horizontal ? b.Left : b.Top;
        double To(Rect b) => horizontal ? b.Right : b.Bottom;
        double CrossFrom(Rect b) => horizontal ? b.Top : b.Left;
        double CrossTo(Rect b) => horizontal ? b.Bottom : b.Right;
        var before = new List<Rect>();
        var after = new List<Rect>();
        foreach (var target in targets)
        {
            if (Math.Min(CrossTo(moving), CrossTo(target)) <= Math.Max(CrossFrom(moving), CrossFrom(target))) continue;
            if (To(target) <= From(moving) + tolerance) before.Add(target);
            if (From(target) >= To(moving) - tolerance) after.Add(target);
        }
        before.Sort((a,b) => To(b).CompareTo(To(a)));
        after.Sort((a,b) => From(a).CompareTo(From(b)));
        (Point A, Point B)[] hints = [];
        double best = tolerance, shift = 0;
        (Point, Point) Bar(Rect a, Rect b)
        {
            double cross = (Math.Max(CrossFrom(a), CrossFrom(b)) + Math.Min(CrossTo(a), CrossTo(b))) / 2;
            return horizontal ? (new Point(To(a), cross), new Point(From(b), cross)) : (new Point(cross, To(a)), new Point(cross, From(b)));
        }
        void Try(double gap, double d, Func<Rect, (Point, Point)[]> bars)
        {
            if (gap <= 0.05 || Math.Abs(d) >= best) return;
            best = Math.Abs(d);
            shift = d;
            var at = moving;
            at.Offset(horizontal ? d : 0, horizontal ? 0 : d);
            hints = bars(at);
        }
        if (before.Count > 0 && after.Count > 0)
        {
            var l = before[0]; var r = after[0];
            double gap = (From(r) - To(l) - (To(moving) - From(moving))) / 2;
            Try(gap, To(l) + gap - From(moving), b => [Bar(l, b), Bar(b, r)]);
        }
        if (before.Count >= 2)
        {
            var l = before[0]; var ll = before[1];
            double gap = From(l) - To(ll);
            Try(gap, To(l) + gap - From(moving), b => [Bar(ll, l), Bar(l, b)]);
        }
        if (after.Count >= 2)
        {
            var r = after[0]; var rr = after[1];
            double gap = From(rr) - To(r);
            Try(gap, From(r) - gap - To(moving), b => [Bar(b, r), Bar(r, rr)]);
        }
        return (shift, hints);
    }

    private void DrawGrid(DrawingContext dc)
    {
        if (!ShowGrid || GridStep <= 0) return;
        // Skip minute divisions rather than drawing thousands of nearly coincident lines.
        double density = Math.Max(1, Math.Ceiling(5 / (GridStep * CanvasScale())));
        double step = GridStep * Math.Max(density, Math.Ceiling(Math.Max(Width, Height) / GridStep / 1500));
        if (!double.IsFinite(step)) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (double x = step; x < Width; x += step) { ctx.BeginFigure(new Point(x, 0), false, false); ctx.LineTo(new Point(x, Height), true, false); }
            for (double y = step; y < Height; y += step) { ctx.BeginFigure(new Point(0, y), false, false); ctx.LineTo(new Point(Width, y), true, false); }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Brushes.LightGray, Math.Min(0.18, 1 / CanvasScale())), geometry);
    }

    private (int Index, bool Vertical)? HitCanvasGuide(Point p)
    {
        if (!ShowGuides || figure?.Guides is not { Length: > 0 } guides) return null;
        double tolerance = Math.Min(2, 4 / CanvasScale());
        (int Index, bool Vertical)? closest = null;
        for (int i = 0; i < guides.Length; i++)
        {
            bool vertical = guides[i].X.HasValue;
            if ((guides[i].X ?? guides[i].Y) is not { } value) continue;
            double distance = Math.Abs((vertical ? p.X : p.Y) - value);
            if (distance < tolerance) { tolerance = distance; closest = (i, vertical); }
        }
        return closest;
    }

    private bool StartCanvasGuide(Point p)
    {
        if (HitCanvasGuide(p) is not { } guide || !CaptureMouse()) return false;
        canvasGuide = guide;
        guidePoint = p;
        start = p;
        delta = default;
        return true;
    }

    private bool FinishCanvasGuide(Point point)
    {
        if (canvasGuide is not { } guide) return false;
        var from = start ?? point;
        ResetDrag();
        ReleaseMouseCapture();
        InvalidateVisual();
        double value = guide.Vertical ? point.X : point.Y;
        if ((point - from).Length >= 0.15)
            GuideEdited?.Invoke(guide.Index, new Rect(0, 0, Width, Height).Contains(point) ? Mm(value) : null);
        return true;
    }

    private Point SnapToolPoint(Point p)
    {
        if (!SnapGrid || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return p;
        return new Point(Math.Round(p.X / GridStep) * GridStep, Math.Round(p.Y / GridStep) * GridStep);
    }
}
