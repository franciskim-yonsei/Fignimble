using System.Windows.Media;

namespace Fignimble.Windows;

public partial class MainWindow
{
    // Match the engine's background preview for a figure that hasn't been viewed yet.
    private const double InitialPreviewDensity = 4;
    private static readonly double[] PreviewDensities = [2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];
    private double CurrentPreviewDensity => currentRender?.PxPerMM is > 0 and var density ? density : InitialPreviewDensity;

    // Zoom scales the displayed sprites. Only an explicit render needs new sprites at the zoom level.
    private double PreviewDensityForView()
    {
        if (!validFigure || currentRender?.Size is not [var width, var height] ||
            Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return CurrentPreviewDensity;
        var dpi = VisualTreeHelper.GetDpi(Viewport);
        double pixelsPerMillimeter = zoom * FitScale(width, height) * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);
        return PreviewDensities.FirstOrDefault(px => px >= pixelsPerMillimeter * 0.9, PreviewDensities[^1]);
    }

    private Task<T> CallForCurrentFigure<T>(string command, object arguments) =>
        engine!.CallAsync<T>(command, arguments, CurrentPreviewDensity);
}
