using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private async void NewProjectClicked(object sender, RoutedEventArgs e)
    {
        JournalChoice[] choices = [];
        await During("Loading project presets…", async () => { choices = await Journals(); Status.Text = "Choose a project preset."; });
        if (choices.Length == 0) return;
        var setup = new FormDialog(this, "New project");
        var journal = setup.Choice("Shared project style", choices.Prepend(GeneralJournal),
            choices.FirstOrDefault(j => j.Key == "nature") ?? GeneralJournal);
        if (!setup.Confirm("Choose folder…", () => null)) return;
        var dialog = new OpenFolderDialog { Title = "Choose or create an empty folder for the project" };
        if (project is not null) dialog.InitialDirectory = Path.GetDirectoryName(project);
        if (dialog.ShowDialog(this) == true) await CreateProject(dialog.FolderName, ((JournalChoice)journal.SelectedItem).Key);
    }

    private Task CreateProject(string folder, string journal) => During("Creating project…", async () =>
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException("The project folder must be empty. Choose or create a different folder.");
        var result = await GetEngine().CallAsync<NewProjectResult>("new_project", new { dir = folder, journal });
        await OpenProjectCore(result.Project, result.Figure);
    });

    private async void NewFigureClicked(object sender, RoutedEventArgs e)
    {
        if (project is null) return;
        var dialog = new NewFigureDialog(currentRender?.Size) { Owner = this };
        if (dialog.ShowDialog() == true) await CreateFigure(dialog.FigureName, dialog.FigureWidth, dialog.FigureHeight);
    }

    private Task CreateFigure(string name, double width, double height) => During("Creating figure…", async () =>
    {
        var result = await engine!.CallAsync<NewFigureResult>("new_figure", new { name, width, height });
        await RefreshProject(result.Name);
    });

    private async void SampleClicked(object sender, RoutedEventArgs e) => await AddSample();

    private Task AddSample() => AddPanel("new_panel", "Adding sample plot…", (figure, box) =>
        new { figure, name = "sample_plot", x = box.X, y = box.Y, w = box.Width, h = box.Height });

    private async void ImportClicked(object sender, RoutedEventArgs e)
    {
        if (project is null || !validFigure) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import a panel, image, or table", InitialDirectory = project,
            Filter = "Supported files|*.py;*.png;*.jpg;*.jpeg;*.svg;*.pdf;*.tif;*.tiff;*.csv;*.tsv|Python panels|*.py" +
                "|Images|*.png;*.jpg;*.jpeg;*.svg;*.pdf;*.tif;*.tiff|Tables|*.csv;*.tsv"
        };
        if (dialog.ShowDialog(this) == true) await ImportPanel(dialog.FileName);
    }

    private Task ImportPanel(string path) => AddPanel("import_file", "Importing…", (figure, box) =>
        new { figure, path, x = box.X, y = box.Y, w = box.Width, h = box.Height });

    private Task AddPanel(string command, string status, Func<string, Rect, object> args)
    {
        if (!validFigure || currentRender is not { } render) return Task.CompletedTask;
        return During(status, async () =>
        {
            var result = await engine!.CallAsync<EditResult>(command, args(render.Figure, FindPlacement(render)));
            ShowRender(result.Figure);
            Page.Select(result.Select?.FirstOrDefault());
            await ReadState(render.Figure);
        });
    }

    internal static Rect FindPlacement(FigureRender figure)
    {
        double pageW = figure.Size![0], pageH = figure.Size[1];
        double gap = Math.Min(5, Math.Min(pageW, pageH) * 0.1);
        double width = Math.Min(60, pageW - 2 * gap), height = Math.Min(45, pageH - 2 * gap);
        Rect[] occupied = (figure.Elements ?? []).Where(e => e.Props is not { Hidden: true } && FigureCanvas.HasBox(e))
            .Select(FigureCanvas.Box).ToArray();
        // Place at the upper-left or just beyond an existing box edge, going left to right and then down.
        var xs = occupied.Select(b => Math.Max(gap, Math.Ceiling(b.Right + gap))).Append(gap)
            .Distinct().Where(x => x + width <= pageW - gap + 0.01).Order().ToArray();
        var ys = occupied.Select(b => Math.Max(gap, Math.Ceiling(b.Bottom + gap))).Append(gap)
            .Distinct().Where(y => y + height <= pageH - gap + 0.01).Order();
        foreach (double y in ys)
            foreach (double x in xs)
            {
                var box = new Rect(x, y, width, height);
                if (occupied.All(b => !b.IntersectsWith(box))) return box;
            }
        return new Rect(gap, gap, width, height); // The page is full; leave the new, selected panel on top.
    }

    private void SyncGeometry()
    {
        var selected = Page.Selection;
        int count = Page.SelectedIds.Count;
        bool page = count == 0;
        bool panel = selected is not null && FigureCanvas.Resizable(selected);
        Geometry.Header = page ? "Page geometry (mm)" : panel ? "Layer geometry (mm)" : count == 1 ? "Layer" : $"{count} selected layers";
        XField.Visibility = YField.Visibility = panel ? Visibility.Visible : Visibility.Collapsed;
        XInput.IsEnabled = YInput.IsEnabled = panel;
        WidthInput.IsEnabled = ApplyButton.IsEnabled = page || panel;
        HeightInput.IsEnabled = page || panel && selected?.Kind != "qr";
        FitButton.Visibility = PresetButton.Visibility = page ? Visibility.Visible : Visibility.Collapsed;
        FitButton.IsEnabled = currentRender?.Elements?.Any(FigureCanvas.HasBox) == true;
        RemoveButton.Visibility = count == 1 ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = selected is not null && FigureCanvas.Unlocked(selected);
        bool details = selected?.Kind is "panel" or "image" or "text" or "line" or "bracket" or "qr";
        DetailsButton.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
        DetailsButton.IsEnabled = details && FigureCanvas.Unlocked(selected!);
        DetailsButton.Content = selected?.Kind is "panel" or "image" ? "Label…" : "Edit…";
        XInput.Text = panel ? FormatMm(selected!.Box![0]) : "";
        YInput.Text = panel ? FormatMm(selected!.Box![1]) : "";
        WidthInput.Text = FormatMm(panel ? selected!.Box![2] : page ? currentRender?.Size?[0] : null);
        HeightInput.Text = FormatMm(panel ? selected!.Box![3] : page ? currentRender?.Size?[1] : null);
    }

    private async void ApplyGeometryClicked(object sender, RoutedEventArgs e) => await ApplyGeometry();

    private Task ApplyGeometry()
    {
        if (!validFigure || currentRender?.Size is not [var width, var height] || shownFigure is not { } figure)
            return Task.CompletedTask;
        var panel = Page.Selection;
        if (Page.SelectedIds.Count > 0 && (panel is null || !FigureCanvas.Resizable(panel))) return Task.CompletedTask;
        var props = new Dictionary<string, double>();
        double[] current = panel?.Box ?? [0, 0, width, height];
        (string key, TextBox field, int i)[] fields =
            [("x", XInput, 0), ("y", YInput, 1), (panel is null ? "width" : "w", WidthInput, 2),
                (panel is null ? "height" : "h", HeightInput, 3)];
        foreach (var (key, field, i) in fields)
        {
            if (panel is null && i < 2) continue;
            if (!TryMm(field.Text, i >= 2, out double value))
            {
                Status.Text = i >= 2 ? "Width and height must be positive numbers in millimeters." : "X and Y must be numbers in millimeters.";
                field.Focus();
                return Task.CompletedTask;
            }
            if (field.Text.Trim() != FormatMm(current[i]) && Math.Abs(value - current[i]) > 0.00001) props.Add(key, value);
        }
        if (props.Count == 0) return Task.CompletedTask;
        object args = panel switch
        {
            null => new { figure, op = "figure", props },
            { Kind: "qr" } => new { figure, op = "set", oid = panel.Oid,
                props = props.Where(p => p.Key != "h").ToDictionary() },
            { Kind: "inset" or "figure" } => new { figure, op = "resize", oid = panel.Oid, box = new[]
                { props.GetValueOrDefault("x", current[0]), props.GetValueOrDefault("y", current[1]),
                    props.GetValueOrDefault("w", current[2]), props.GetValueOrDefault("h", current[3]) } },
            _ => new { figure, op = "set", oid = panel.Oid, props }
        };
        return EditFigure(figure, args, "Applying geometry…");
    }

    private async void FitClicked(object sender, RoutedEventArgs e) => await FitPage();

    private Task FitPage() => !validFigure || shownFigure is not { } figure ? Task.CompletedTask :
        EditFigure(figure, new { figure, op = "fit_figure", margin = 5 }, "Fitting page to content…");

    private async void RemoveClicked(object sender, RoutedEventArgs e)
    {
        if (!validFigure || shownFigure is not { } figure || Page.Selection is not { } selected || !FigureCanvas.Unlocked(selected)) return;
        await EditFigure(figure, new { figure, op = "delete", oids = new[] { selected.Oid } }, "Removing layer…",
            clearSelection: true);
    }

    private void ShowWarnings(FigureWarning[]? warnings)
    {
        var messages = (warnings ?? []).Select(w => $"{w.Level}: {w.Message}").ToArray();
        Checks.Header = messages.Length > 0 ? $"Preflight ({messages.Length})" : "Preflight";
        Warnings.ItemsSource = messages.Length > 0 ? messages : ["No preflight messages."];
    }

    internal static string FormatMm(double? value) => value?.ToString("0.####", CultureInfo.CurrentCulture) ?? "";

    internal static bool TryMm(string text, bool positive, out double value) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        double.IsFinite(value) && (!positive || value > 0);
}
