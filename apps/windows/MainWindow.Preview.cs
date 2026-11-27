using System.Windows.Media;
using System.Windows.Threading;

namespace Fignimble.Windows;

public partial class MainWindow
{
    // Match the engine's background preview for a figure that hasn't been viewed yet.
    private const double InitialPreviewDensity = 4;
    private static readonly double[] PreviewDensities = [2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private double CurrentPreviewDensity => currentRender?.PxPerMM is > 0 and var density ? density : InitialPreviewDensity;

    private void SetupPreview()
    {
        DpiChanged += (_, _) => SchedulePreview();
        previewTimer.Tick += async (_, _) =>
        {
            if (busy || reloadTimer.IsEnabled || !IsActive || Page.IsDragging || CanvasEditors.Children.Count > 0 ||
                Geometry.IsKeyboardFocusWithin || PropertyEditor.IsKeyboardFocusWithin ||
                SourceText.IsKeyboardFocusWithin || CaptionText.IsKeyboardFocusWithin) return;
            previewTimer.Stop();
            if (validFigure && shownFigure is { } figure && engine is { Running: true } &&
                PreviewDensityForView() != CurrentPreviewDensity)
                await During("Updating preview…", () => RenderFigure(figure, external: true));
        };
    }

    private void SchedulePreview()
    {
        previewTimer.Stop();
        if (PreviewDensityForView() != CurrentPreviewDensity) previewTimer.Start();
    }

    private double PreviewDensityForView()
    {
        double current = CurrentPreviewDensity;
        if (!validFigure || currentRender?.Size is not [var width, var height] ||
            Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return current;
        var dpi = VisualTreeHelper.GetDpi(Viewport);
        double pixelsPerMillimeter = zoom * FitScale(width, height) * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);
        return AdjustPreviewDensity(pixelsPerMillimeter, current);
    }

    private static double AdjustPreviewDensity(double pixelsPerMillimeter, double current)
    {
        // Use the macOS resolution steps and leave room for small zoom and window-size changes.
        double desired = PreviewDensities.FirstOrDefault(px => px >= pixelsPerMillimeter * 0.9, PreviewDensities[^1]);
        return desired > current * 1.4 || desired < current / 2.5 ? desired : current;
    }

    private Task<T> CallForCurrentFigure<T>(string command, object arguments) =>
        engine!.CallAsync<T>(command, arguments, CurrentPreviewDensity);
}
