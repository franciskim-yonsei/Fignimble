using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private readonly List<FigureCard> figureCards = [];
    private readonly DispatcherTimer thumbnailTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private GridLength drawerHeight = new(185);
    private static readonly DropShadowEffect PaperShadow = CreatePaperShadow();

    private static DropShadowEffect CreatePaperShadow()
    {
        var shadow = new DropShadowEffect { Color = Color.FromRgb(31, 46, 49), BlurRadius = 13, ShadowDepth = 2, Opacity = 0.28 };
        shadow.Freeze();
        return shadow;
    }

    private sealed class FigureCard(string name) : INotifyPropertyChanged
    {
        private string? thumb;
        private DateTime stamp;
        public string Name { get; } = name;
        public BitmapSource? Preview { get; private set; }
        public string Indicator { get; private set; } = "";
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Update(string? path, string? error, int warnings)
        {
            string indicator = error is not null ? "Error" : warnings > 0 ? $"⚠ {warnings}" : "";
            if (indicator != Indicator)
            {
                Indicator = indicator;
                PropertyChanged?.Invoke(this, new(nameof(Indicator)));
            }
            if (error is not null) path = null;
            try
            {
                var modified = path is null ? default : File.GetLastWriteTimeUtc(path);
                if (thumb == path && stamp == modified) return;
                BitmapSource? image = null;
                if (path is not null)
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    image.Freeze();
                }
                thumb = path;
                stamp = modified;
                Preview = image;
                PropertyChanged?.Invoke(this, new(nameof(Preview)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (NotSupportedException) { }
            catch (FileFormatException) { }
        }
    }

    private void SetupWorkspace()
    {
        SourceText.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(".py");
        RefreshFigureEmpty();
        thumbnailTimer.Tick += async (_, _) =>
        {
            if (busy || editingSource || Page.IsDragging) return;
            thumbnailTimer.Stop();
            if (project is not { } folder || engine is not { Running: true } client) return;
            try
            {
                var figures = (await client.CallAsync<ProjectState>("state")).Figures;
                if (busy || engine != client || project != folder) return;
                foreach (var figure in figures) UpdateFigureCard(figure.Name, figure.Thumb, figure.Error, figure.Warnings);
            }
            catch (Exception) { /* A thumbnail refresh must not interrupt the current edit. */ }
        };
    }

    private void SetFigureCards(FigureEntry[] entries, string? preferred)
    {
        if (!figureCards.Select(f => f.Name).SequenceEqual(entries.Select(e => e.Name)))
        {
            var previous = figureCards.ToDictionary(f => f.Name);
            figureCards.Clear();
            figureCards.AddRange(entries.Select(f => previous.TryGetValue(f.Name, out var card) ? card : new FigureCard(f.Name)));
            Figures.ItemsSource = figureCards.ToArray();
        }
        foreach (var entry in entries) UpdateFigureCard(entry.Name, entry.Thumb, entry.Error, entry.Warnings);
        Figures.SelectedItem = figureCards.FirstOrDefault(f => f.Name == preferred) ?? figureCards.FirstOrDefault();
        RefreshFigureEmpty();
    }

    private void UpdateFigureCard(string name, string? thumb, string? error, int warnings) =>
        figureCards.FirstOrDefault(f => f.Name == name)?.Update(thumb, error, warnings);

    private void RefreshFigureEmpty()
    {
        bool hasCards = figureCards.Count > 0;
        Figures.Visibility = hasCards ? Visibility.Visible : Visibility.Collapsed;
        FiguresEmpty.Visibility = hasCards ? Visibility.Collapsed : Visibility.Visible;
        FiguresEmpty.Text = project is null ? "Project figures will appear here." : "No figures yet. Use + to add one.";
    }

    private void ToggleDrawerClicked(object sender, RoutedEventArgs e)
    {
        if (DrawerTabs.Visibility == Visibility.Collapsed) ShowDrawer(DrawerTabs.SelectedItem as TabItem ?? CodeTab);
        else
        {
            drawerHeight = DrawerRow.ActualHeight >= 75 ? new GridLength(DrawerRow.ActualHeight) : new GridLength(185);
            DrawerTabs.Visibility = DrawerSplitter.Visibility = Visibility.Collapsed;
            DrawerRow.MinHeight = 0;
            DrawerRow.Height = DrawerSplit.Height = new GridLength(0);
            DrawerButton.Content = "Show drawer";
        }
    }

    private void ShowDrawer(TabItem tab)
    {
        if (DrawerTabs.Visibility == Visibility.Collapsed)
        {
            DrawerTabs.Visibility = DrawerSplitter.Visibility = Visibility.Visible;
            DrawerRow.MinHeight = 75;
            DrawerRow.Height = drawerHeight;
            DrawerSplit.Height = new GridLength(4);
            DrawerButton.Content = "Hide drawer";
        }
        if (DrawerTabs.SelectedItem != tab) DrawerTabs.SelectedItem = tab;
        else SyncDrawer();
    }

    private void DrawerTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == DrawerTabs && TerminalTab is not null) SyncDrawer();
    }

    private void SyncDrawer()
    {
        if (!TerminalRequested || project is null || engine is null) return;
        if (terminalFolder != project) StartShell();
        else terminal?.FocusTerminal();
    }

    private void UpdateInspector()
    {
        var layer = Page.Selection;
        int count = Page.SelectedIds.Count;
        InspectorTitle.Text = count > 1 ? $"{count} layers" : layer?.Props?.Name ??
            (layer?.Props?.Source is { } path ? Path.GetFileName(path) : layer?.Method ?? (currentRender?.Poster is null ? "Page" : "Poster"));
        InspectorHint.Text = count > 1 ? "Move or arrange the selection together." : layer is null
            ? (validFigure ? "Page size and shared style" : "Open or create a figure to edit its settings.")
            : layer.Props is { Locked: true } ? "Locked layer" : layer.Props is { Hidden: true } ? "Hidden layer"
            : layer.Kind == "panel" ? "Python panel · redraws when resized" : $"{layer.Kind ?? "Figure"} layer";
        InspectorActionsTitle.Text = count == 0 ? "PROJECT STYLE" : "SELECTED LAYER";
        void Action(Button b, string? caption, string tag)
        {
            b.Content = caption;
            b.Tag = tag;
            b.Visibility = caption is null || !validFigure ? Visibility.Collapsed : Visibility.Visible;
        }
        var options = !validFigure ? (null, "", null, "") : count > 1 ? ("Arrange ▾", "arrange", null, "")
            : layer is null ? ("Plot style…", "style", "Panel lettering…", "labels")
            : !FigureCanvas.Unlocked(layer) ? (null, "", null, "") : layer.Kind switch
            {
                "panel" => ("Edit source…", "source", layer.Texts is { Length: > 0 } ? "Plot text…" : null, "plot-text"),
                "image" => ("Crop…", "crop", "Rotate…", "rotate"),
                "table" => ("Edit cells…", "table", "Export table…", "export-table"),
                "layout" => ("Release layout", "release", null, ""),
                _ => (null, "", null, "")
            };
        Action(InspectorFirst, options.Item1, options.Item2);
        Action(InspectorSecond, options.Item3, options.Item4);
        InspectorActionsTitle.Visibility = options.Item1 is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void InspectorActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        if (key == "arrange") { ArrangeClicked(sender, e); return; }
        await (key switch
        {
            "style" => EditStyle(false), "labels" => EditStyle(true), "source" => EditSource(), "plot-text" => EditPlotText(),
            "crop" => CropImage(), "rotate" => RotateImage(), "table" => EditTableCells(), "export-table" => ChooseTableExport(),
            "release" => ReleaseLayout(), _ => Task.CompletedTask
        });
    }
}
