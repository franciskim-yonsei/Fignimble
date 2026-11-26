using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Fignimble.Windows;

public sealed partial class FigureCanvas
{
    private (int X, int Y)? resizeGrip;
    private (string Oid, Point From, Point Other, bool First)? endpoint;
    private (string Oid, int Index, Point[] Points)? pathPoint;

    private static Point[] PathPoints(FigureElement element) => element.Kind == "path" &&
        element.Props?.Extra?.GetValueOrDefault("points") is { ValueKind: System.Text.Json.JsonValueKind.Array } points
        ? points.EnumerateArray().Where(p => p.ValueKind == System.Text.Json.JsonValueKind.Array && p.GetArrayLength() == 2)
            .Select(p => new Point(p[0].GetDouble(), p[1].GetDouble())).ToArray() : [];

    private bool BeginPathPoint(Point at)
    {
        if (Selection is not { } path || !Movable(path)) return false;
        var points = PathPoints(path);
        int index = Array.FindIndex(points, p => GripBounds(p).Contains(at));
        if (index < 0 || !CaptureMouse()) return false;
        pathPoint = (path.Oid, index, points);
        start = at;
        delta = default;
        return true;
    }
    internal event Action<string, object>? HandleEdited;
    private string? cropOid;
    private bool Cropping => cropOid is not null && cropOid == Selection?.Oid;

    internal void BeginCrop(string oid) { cropOid = oid; InvalidateVisual(); }
    internal bool CancelCrop()
    {
        if (cropOid is null) return false;
        cropOid = null;
        if (resizeGrip is not null) ReleaseMouseCapture();
        InvalidateVisual();
        return true;
    }

    private void DrawCropMask(DrawingContext dc, Rect original, Rect crop)
    {
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(original), new RectangleGeometry(crop));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(112, 0, 0, 0)), null, outside);
    }

    private IEnumerable<(int X, int Y)> Grips(FigureElement e)
    {
        if (!Resizable(e) && !(Movable(e) && e.Kind == "text")) yield break;
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && (y == 0 || (!Cropping && e.Kind is "text" or "image" or "figure" or "qr"))) continue;
                if (e.Kind == "qr" && y == 0) continue;
                yield return (x, y);
            }
    }

    private static Point GripPoint(Rect box, (int X, int Y) grip) => new(
        grip.X < 0 ? box.Left : grip.X > 0 ? box.Right : box.Left + box.Width / 2,
        grip.Y < 0 ? box.Top : grip.Y > 0 ? box.Bottom : box.Top + box.Height / 2);

    private Rect GripBounds(Point point)
    {
        double edge = Math.Min(2, 6 / CanvasScale());
        return new Rect(point.X - edge / 2, point.Y - edge / 2, edge, edge);
    }

    private double CanvasScale() => PresentationSource.FromVisual(this) is null ? 1 :
        Math.Max(0.01, (PointToScreen(new Point(1, 0)) - PointToScreen(new Point())).Length);

    private (int X, int Y)? HitGrip(FigureElement el, Point p)
    {
        foreach (var grip in Grips(el))
            if (GripBounds(GripPoint(Box(el), grip)).Contains(p)) return grip;
        return null;
    }

    private static (Point Start, Point End)? LineEnds(FigureElement e) =>
        e.Kind == "line" && Movable(e) && e.Props is { X1: { } x1, Y1: { } y1, X2: { } x2, Y2: { } y2 }
            ? (new Point(x1, y1), new Point(x2, y2)) : null;

    private bool BeginEndpoint(Point at)
    {
        if (Selection is not { } line || LineEnds(line) is not { } ends) return false;
        bool first = GripBounds(ends.Start).Contains(at);
        if (!first && !GripBounds(ends.End).Contains(at) || !CaptureMouse()) return false;
        endpoint = (line.Oid, first ? ends.Start : ends.End, first ? ends.End : ends.Start, first);
        start = at;
        delta = default;
        return true;
    }

    private Point DraggedEndpoint((string Oid, Point From, Point Other, bool First) move)
    {
        var target = move.From + delta;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return target;
        Vector vector = target - move.Other;
        double angle = Math.Round(Math.Atan2(vector.Y, vector.X) / (Math.PI / 4)) * Math.PI / 4;
        return move.Other + new Vector(Math.Cos(angle), Math.Sin(angle)) * vector.Length;
    }

    private void DrawGrips(DrawingContext dc, FigureElement active, Rect box)
    {
        if (LineEnds(active) is { } ends)
        {
            if (endpoint is { } moving)
            {
                Point p = DraggedEndpoint(moving);
                dc.DrawLine(SelectionPen, moving.Other, p);
                ends = moving.First ? (p, moving.Other) : (moving.Other, p);
            }
            else if (start.HasValue && !marquee && dividerDrag is null)
                ends = (ends.Start + delta, ends.End + delta);
            dc.DrawRectangle(Accent, null, GripBounds(ends.Start));
            dc.DrawRectangle(Accent, null, GripBounds(ends.End));
        }
        else if (active.Kind == "path" && Movable(active) && resizeGrip is null)
        {
            var points = PathPoints(active);
            if (pathPoint is { } vertex)
            {
                points[vertex.Index] = SnapToolPoint(vertex.Points[vertex.Index] + delta);
                for (int i = 1; i < points.Length; i++) dc.DrawLine(SelectionPen, points[i - 1], points[i]);
            }
            else if (start.HasValue && !marquee && canvasGuide is null)
                points = points.Select(p => p + delta).ToArray();
            foreach (var point in points) dc.DrawRectangle(Accent, null, GripBounds(point));
        }
        else foreach (var grip in Grips(active)) dc.DrawRectangle(Accent, null, GripBounds(GripPoint(box, grip)));
    }

    private static Cursor ResizeCursor((int X, int Y) grip) => grip.X == 0 ? Cursors.SizeNS : grip.Y == 0 ? Cursors.SizeWE
        : grip.X == grip.Y ? Cursors.SizeNWSE : Cursors.SizeNESW;

    private Rect ResizeBox(FigureElement el, Rect box, (int X, int Y) grip)
    {
        double w = Math.Max(1, box.Width + grip.X * delta.X), h = Math.Max(1, box.Height + grip.Y * delta.Y);
        bool natural = el.Kind is "image" or "figure" && el.Props?.H is null;
        bool aspect = !Cropping && (natural || el.Kind is "qr" || grip.Y != 0 && el.Kind == "text" || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        if (aspect && box.Width > 0 && box.Height > 0)
        {
            double ratio = grip.X == 0 ? h / box.Height : grip.Y == 0 || !natural && el.Kind == "text" ? w / box.Width
                : Math.Abs(w / box.Width - 1) >= Math.Abs(h / box.Height - 1) ? w / box.Width : h / box.Height;
            ratio = Math.Max(ratio, Math.Max(1 / box.Width, 1 / box.Height));
            w = box.Width * ratio;
            h = box.Height * ratio;
        }
        return new Rect(grip.X < 0 ? box.Right - w : grip.X > 0 ? box.X : box.X + (box.Width - w) / 2,
            grip.Y < 0 ? box.Bottom - h : grip.Y > 0 ? box.Y : box.Y + (box.Height - h) / 2, w, h);
    }
}
