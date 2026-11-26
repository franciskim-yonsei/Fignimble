using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Fignimble.Windows;

internal enum CanvasTool { Select, Text, Line, Arrow, Rect, Ellipse, Bracket, Pen, Zoom }

public sealed partial class FigureCanvas
{
    internal CanvasTool Tool { get; private set; }
    internal event Action<CanvasTool>? ToolChanged;
    internal event Action<string, object>? ShapeDrawn;
    internal event Action<Point, FigureElement?>? TextRequested;
    internal event Action<FigureElement>? ElementOpened;
    internal event Action<Rect, bool>? ZoomRequested;
    private readonly List<Point> penPoints = [];
    private Point? toolAnchor, toolEnd;
    private bool keepTool;
    private bool IsDrawingTool => toolAnchor.HasValue;

    internal void SetTool(CanvasTool tool)
    {
        if (tool == Tool) return;
        CancelTool();
        Tool = tool;
        Cursor = tool switch { CanvasTool.Select => Cursors.Arrow, CanvasTool.Text => Cursors.IBeam, _ => Cursors.Cross };
        ToolChanged?.Invoke(tool);
    }

    internal bool CancelTool()
    {
        bool active = Tool != CanvasTool.Select || toolAnchor.HasValue || penPoints.Count > 0;
        bool captured = toolAnchor.HasValue;
        toolAnchor = toolEnd = null;
        penPoints.Clear();
        if (captured) ReleaseMouseCapture();
        InvalidateVisual();
        return active;
    }

    internal void FinishPen(bool closed)
    {
        var points = penPoints.Select(p => new[] { Mm(p.X), Mm(p.Y) }).ToArray();
        penPoints.Clear();
        toolEnd = null;
        InvalidateVisual();
        if (points.Length >= 2)
        {
            CompleteTool();
            ShapeDrawn?.Invoke("path", new { points, closed = closed && points.Length >= 3 });
        }
    }

    private bool ToolDown(MouseButtonEventArgs e, Point p)
    {
        if (Tool == CanvasTool.Select) return false;
        keepTool = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (Tool != CanvasTool.Zoom) p = SnapToolPoint(p);
        if (Tool == CanvasTool.Text)
        {
            CompleteTool();
            TextRequested?.Invoke(p, null);
        }
        else if (Tool == CanvasTool.Pen)
        {
            if (penPoints.Count >= 3 && CloseTo(p, penPoints[0])) FinishPen(true);
            else if (e.ClickCount == 2) FinishPen(false);
            else { penPoints.Add(p); toolEnd = p; InvalidateVisual(); }
        }
        else if (Tool == CanvasTool.Zoom && keepTool)
            ZoomRequested?.Invoke(new Rect(p, p), true);
        else if (CaptureMouse())
        {
            toolAnchor = toolEnd = p;
            InvalidateVisual();
        }
        return true;
    }

    private bool CloseTo(Point p, Point q)
    {
        var a = PointToScreen(p);
        var b = PointToScreen(q);
        return (a - b).Length <= 7;
    }

    private bool ToolMove(Point p)
    {
        if (Tool == CanvasTool.Select) return false;
        if (Tool != CanvasTool.Zoom) p = SnapToolPoint(p);
        toolEnd = toolAnchor is { } anchor ? ConstrainTool(anchor, p) : p;
        InvalidateVisual();
        return true;
    }

    private Point ConstrainTool(Point from, Point to)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return to;
        Vector d = to - from;
        if (Tool is CanvasTool.Line or CanvasTool.Arrow)
        {
            double angle = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * Math.PI / 4;
            return from + new Vector(Math.Cos(angle), Math.Sin(angle)) * d.Length;
        }
        if (Tool is CanvasTool.Rect or CanvasTool.Ellipse)
        {
            double side = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
            return from + new Vector(Math.CopySign(side, d.X), Math.CopySign(side, d.Y));
        }
        return to;
    }

    private bool ToolUp(Point p)
    {
        if (Tool == CanvasTool.Select) return false;
        if (toolAnchor is not { } from) return true;
        Point to = ConstrainTool(from, Tool == CanvasTool.Zoom ? p : SnapToolPoint(p));
        var tool = Tool;
        toolAnchor = toolEnd = null;
        ReleaseMouseCapture();
        CompleteTool();
        InvalidateVisual();
        if (tool == CanvasTool.Zoom) ZoomRequested?.Invoke(new Rect(from, to), false);
        else EmitShape(tool, from, to);
        return true;
    }

    private void CompleteTool()
    {
        if (!keepTool) SetTool(CanvasTool.Select);
    }

    private static double Mm(double length) => Math.Round(length, 2);

    private void EmitShape(CanvasTool tool, Point from, Point to)
    {
        var rect = new Rect(from, to);
        bool click = rect.Width < 0.5 && rect.Height < 0.5;
        string kind = tool.ToString().ToLowerInvariant();
        if (tool is CanvasTool.Rect or CanvasTool.Ellipse)
        {
            if (click) rect = new Rect(from.X, from.Y, 20, 12);
            ShapeDrawn?.Invoke(kind, new { x = Mm(rect.X), y = Mm(rect.Y), w = Mm(Math.Max(0.5, rect.Width)), h = Mm(Math.Max(0.5, rect.Height)) });
        }
        else if (tool is CanvasTool.Line or CanvasTool.Arrow)
        {
            if (click) to = from + new Vector(15, 0);
            ShapeDrawn?.Invoke(kind, new { x1 = Mm(from.X), y1 = Mm(from.Y), x2 = Mm(to.X), y2 = Mm(to.Y) });
        }
        else if (tool == CanvasTool.Bracket)
        {
            double x = rect.Width < 0.5 ? from.X + 15 : to.X;
            ShapeDrawn?.Invoke(kind, new { x1 = Mm(Math.Min(from.X, x)), x2 = Mm(Math.Max(from.X, x)), y = Mm(from.Y), text = "*" });
        }
    }

    private void DrawToolPreview(DrawingContext dc)
    {
        var pen = new Pen(Accent, 0.35);
        if (penPoints.Count > 0)
        {
            for (int i = 1; i < penPoints.Count; i++) dc.DrawLine(pen, penPoints[i - 1], penPoints[i]);
            dc.DrawEllipse(Accent, null, penPoints[0], 0.85, 0.85);
            if (toolEnd is { } tip) dc.DrawLine(pen, penPoints[^1], tip);
        }
        if (toolAnchor is not { } a || toolEnd is not { } b) return;
        var box = new Rect(a, b);
        switch (Tool)
        {
            case CanvasTool.Ellipse: dc.DrawEllipse(null, pen, new Point(box.X + box.Width / 2, box.Y + box.Height / 2), box.Width / 2, box.Height / 2); break;
            case CanvasTool.Line:
            case CanvasTool.Arrow:
                dc.DrawLine(pen, a, b);
                Vector d = b - a;
                if (Tool == CanvasTool.Arrow && d.Length > 1)
                {
                    d.Normalize();
                    var side = new Vector(-d.Y, d.X);
                    dc.DrawLine(pen, b, b - 2.8 * d + 1.2 * side);
                    dc.DrawLine(pen, b, b - 2.8 * d - 1.2 * side);
                }
                break;
            case CanvasTool.Bracket:
                var far = new Point(b.X, a.Y);
                dc.DrawLine(pen, a, far);
                dc.DrawLine(pen, a, a + new Vector(0, 1.5));
                dc.DrawLine(pen, far, far + new Vector(0, 1.5));
                break;
            default: dc.DrawRectangle(null, pen, box); break;
        }
    }
}
