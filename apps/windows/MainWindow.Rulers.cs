using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private (FigureRuler Ruler, int? Index, FigureRender Render)? dragGuide;
    private bool rulerRefreshQueued;

    private void ViewportScrolled(object sender, ScrollChangedEventArgs e) => ScheduleRulers();

    private void ScheduleRulers()
    {
        if (rulerRefreshQueued || TopRuler is null || LeftRuler is null) return;
        rulerRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            rulerRefreshQueued = false;
            foreach (var ruler in new[] { TopRuler, LeftRuler })
            {
                ruler.LengthMm = 0;
                if (validFigure && PageView.IsVisible && Page.IsLoaded && currentRender?.Size is [var w, var h])
                {
                    Point zero = Page.TranslatePoint(new Point(0, 0), ruler);
                    Point one = Page.TranslatePoint(new Point(1, 1), ruler);
                    ruler.Origin = ruler.Vertical ? zero.Y : zero.X;
                    ruler.Scale = ruler.Vertical ? one.Y - zero.Y : one.X - zero.X;
                    ruler.LengthMm = ruler.Vertical ? h : w;
                    ruler.Marks = Page.ShowGuides ? (currentRender.Guides ?? []).Select(g => ruler.Vertical ? g.Y : g.X).OfType<double>().ToArray() : [];
                }
                ruler.InvalidateVisual();
            }
        });
    }

    private void RulerDown(object sender, MouseButtonEventArgs e)
    {
        if (busy || !validFigure || !Page.ShowGuides || currentRender is not { } render || sender is not FigureRuler ruler || ruler.Scale <= 0) return;
        double px = ruler.Vertical ? e.GetPosition(ruler).Y : e.GetPosition(ruler).X;
        var hits = (render.Guides ?? []).Select((g, i) => (Hint: ruler.Vertical ? g.Y : g.X, Index: i))
            .Where(t => t.Hint.HasValue && Math.Abs(ruler.Origin + t.Hint.Value * ruler.Scale - px) <= 5)
            .OrderBy(t => Math.Abs(ruler.Origin + t.Hint!.Value * ruler.Scale - px)).ToArray();
        if (!ruler.CaptureMouse()) return;
        dragGuide = (ruler, hits.Length > 0 ? hits[0].Index : null, render);
        RulerMove(sender, e);
        e.Handled = true;
    }

    private void RulerMove(object sender, MouseEventArgs e)
    {
        if (dragGuide is not { } drag || sender != drag.Ruler) return;
        Point at = e.GetPosition(Page);
        double coordinate = drag.Ruler.Vertical ? at.Y : at.X;
        var guide = drag.Ruler.Vertical ? new FigureGuide(null, coordinate) : new FigureGuide(coordinate, null);
        Page.PreviewGuide(coordinate >= 0 && coordinate <= drag.Ruler.LengthMm ? guide : null);
    }

    private async void RulerUp(object sender, MouseButtonEventArgs e)
    {
        if (dragGuide is not { } drag || sender != drag.Ruler) return;
        Point at = e.GetPosition(Page);
        double position = Math.Round(drag.Ruler.Vertical ? at.Y : at.X, 2);
        drag.Ruler.ReleaseMouseCapture();
        e.Handled = true;
        if (!StillEditing(drag.Render) || shownFigure is not { } figure) return;
        if (drag.Index is { } index)
        {
            object args = position < 0 || position > drag.Ruler.LengthMm ? new { figure, op = "guide_delete", index }
                : new { figure, op = "guide_move", index, value = position };
            await EditFigure(figure, args, "Updating guide…");
        }
        else if (position >= 0 && position <= drag.Ruler.LengthMm)
            await ChangeGuide("guide_add", drag.Ruler.Vertical ? "y" : "x", position);
    }

    private Task MoveCanvasGuide(int index, double? position)
    {
        if (shownFigure is not { } figure || !validFigure) return Task.CompletedTask;
        return EditFigure(figure, position is { } value ? new { figure, op = "guide_move", index, value } :
            (object)new { figure, op = "guide_delete", index }, "Updating guide…");
    }

    private void RulerLost(object sender, MouseEventArgs e)
    {
        dragGuide = null;
        Page.PreviewGuide(null);
    }
}
