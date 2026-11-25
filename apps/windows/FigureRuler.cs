using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Fignimble.Windows;

public sealed class FigureRuler : FrameworkElement
{
    public bool Vertical { get; set; }
    internal double Origin { get; set; }
    internal double Scale { get; set; }
    internal double LengthMm { get; set; }
    internal double[] Marks { get; set; } = [];

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(RenderSize));
        if (!double.IsFinite(Scale) || Scale <= 0 || LengthMm <= 0) return;
        var ink = new Pen(Brushes.DimGray, 0.6);
        double length = Vertical ? ActualHeight : ActualWidth;
        double breadth = Vertical ? ActualWidth : ActualHeight;
        double target = Math.Max(0.1, 42 / Scale);
        double decade = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double major = decade * (target <= decade ? 1 : target <= 2 * decade ? 2 : target <= 5 * decade ? 5 : 10);
        double minor = major / 5;
        double start = Math.Max(0, Math.Floor(-Origin / minor / Scale) * minor);
        double end = Math.Min(LengthMm, (length - Origin) / Scale);
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        for (double mm = start; mm <= end + 1e-6; mm += minor)
        {
            double at = Origin + mm * Scale;
            bool label = Math.Abs(mm / major - Math.Round(mm / major)) < 1e-6;
            Tick(dc, ink, at, breadth, label ? 6 : 3);
            if (!label) continue;
            var text = new FormattedText(mm.ToString("0.###", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (Vertical)
            {
                dc.PushTransform(new RotateTransform(-90, 0, at - 2));
                dc.DrawText(text, new Point(0, at - 2));
                dc.Pop();
            }
            else dc.DrawText(text, new Point(at + 2, 0));
        }
        var teal = new Pen(Brushes.Teal, 2);
        foreach (double mark in Marks) Tick(dc, teal, Origin + mark * Scale, breadth, 10);
        dc.Pop();
    }

    private void Tick(DrawingContext dc, Pen pen, double at, double breadth, double extent)
    {
        if (Vertical) dc.DrawLine(pen, new Point(breadth - extent, at), new Point(breadth, at));
        else dc.DrawLine(pen, new Point(at, breadth - extent), new Point(at, breadth));
    }
}
