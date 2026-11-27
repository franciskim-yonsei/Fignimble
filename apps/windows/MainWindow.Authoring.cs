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
        await During("Loading project presets…", async () =>
        {
            if (await PrepareLocalPython(null) is null) return;
            choices = await Journals();
            Status.Text = "Choose a project preset.";
        });
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
        if (!ConfirmDiscardDrafts()) return;
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException("The project folder must be empty. Choose or create a different folder.");
        using var setup = new EngineClient(ProjectPython(null));
        var result = await setup.CallAsync<NewProjectResult>("new_project", new { dir = folder, journal });
        await OpenProjectCore(result.Project, result.Figure);
    });

    private async void NewFigureClicked(object sender, RoutedEventArgs e)
    {
        if (project is null || busy) return;
        PosterTemplate[] templates = [];
        await During("Loading figure templates…", async () =>
            templates = (await engine!.CallAsync<PosterTemplates>("poster_templates")).Templates);
        var dialog = new NewFigureDialog(currentRender?.Size, templates.Select(t => (t.Key, t.ToString(), t.Size))) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.PosterTemplate is { } poster) await CreatePoster(dialog.FigureName, poster);
        else await CreateFigure(dialog.FigureName, dialog.FigureWidth, dialog.FigureHeight);
    }

    private Task CreateFigure(string name, double width, double height) => During("Creating figure…", async () =>
    {
        if (!ConfirmDiscardDrafts()) return;
        var result = await engine!.CallAsync<NewFigureResult>("new_figure", new { name, width, height });
        await RefreshProject(result.Name);
    });

    private async void SampleClicked(object sender, RoutedEventArgs e) => await AddSample();

    private Task AddSample() => AddPanel("new_panel", "Adding sample plot…", (figure, box) =>
        new { figure, name = "sample_plot", x = box.X, y = box.Y, w = box.Width, h = box.Height });

    private async void ImportClicked(object sender, RoutedEventArgs e) => await ChooseImport("Import a panel, image, or table",
        "Supported files|*.py;*.png;*.jpg;*.jpeg;*.svg;*.pdf;*.tif;*.tiff;*.csv;*.tsv|Python panels|*.py" +
        "|Images|*.png;*.jpg;*.jpeg;*.svg;*.pdf;*.tif;*.tiff|Tables|*.csv;*.tsv");

    private async Task ChooseImport(string title, string filter)
    {
        if (busy || project is null || !validFigure) return;
        var dialog = new OpenFileDialog { Title = title, InitialDirectory = project, Filter = filter };
        if (dialog.ShowDialog(this) == true) await ImportPanel(dialog.FileName);
    }

    private async Task NewPanelFromCode()
    {
        if (busy || !validFigure || !ConfirmDiscardCode()) return;
        var dialog = new FormDialog(this, "New panel from code");
        var name = dialog.Text("Panel name", "panel");
        if (!dialog.Confirm("Create panel", () => System.Text.RegularExpressions.Regex.IsMatch(name.Text.Trim(), @"\A[A-Za-z][A-Za-z0-9_]*\z")
                ? null : "Start with a letter and use only letters, numbers, or underscores.")) return;
        string stem = name.Text.Trim();
        if (new[] { "AUX", "CON", "NUL", "PRN" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(stem, @"\A(COM|LPT)[1-9]\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        { Status.Text = "Choose a name that is not reserved by Windows."; return; }
        string? created = null;
        await AddPanel("new_panel", "Creating panel…", (figure, box) =>
            new { figure, name = stem, x = box.X, y = box.Y, w = box.Width, h = box.Height }, result => created = result.File);
        if (created is not null)
        {
            ShowDrawer(CodeTab);
            if (OpenCodeFile(created, force: true)) SourceText.Focus();
        }
    }

    private Task ImportPanel(string path) => AddPanel("import_file", "Importing…", (figure, box) =>
        new { figure, path, x = box.X, y = box.Y, w = box.Width, h = box.Height });

    private static readonly HashSet<string> DropExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".py", ".png", ".jpg", ".jpeg", ".svg", ".pdf", ".tif", ".tiff", ".csv", ".tsv" };

    private string[] DroppedFiles(DragEventArgs e) => !busy && validFigure && !Page.IsDragging &&
        e.AllowedEffects.HasFlag(DragDropEffects.Copy) && e.Data.GetData(DataFormats.FileDrop) is string[] paths
        ? paths.Where(p => DropExtensions.Contains(Path.GetExtension(p)) && File.Exists(p)).ToArray() : [];

    private void CanvasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFiles(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void CanvasDrop(object sender, DragEventArgs e)
    {
        var paths = DroppedFiles(e);
        e.Effects = paths.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (paths.Length > 0) await ImportAt(paths, e.GetPosition(Page));
    }

    private Task ImportAt(string[] paths, Point position)
    {
        if (!validFigure || shownFigure is not { } figure) return Task.CompletedTask;
        return During("Importing dropped files…", async () =>
        {
            var failures = new List<string>();
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                double x = Math.Round(position.X + i * 5, 2), y = Math.Round(position.Y + i * 5, 2);
                try
                {
                    var result = await CallForCurrentFigure<EditResult>("import_file", new { figure, path, x, y });
                    ShowRender(result.Figure);
                    Page.SelectIds(result.Select ?? []);
                }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
            }
            await ReadState(figure);
            if (failures.Count > 0) Status.Text = string.Join(" · ", failures);
        });
    }

    private Task AddPanel(string command, string status, Func<string, Rect, object> args, Action<EditResult>? receive = null)
    {
        if (!validFigure || currentRender is not { } render) return Task.CompletedTask;
        return During(status, async () =>
        {
            var result = await CallForCurrentFigure<EditResult>(command, args(render.Figure, FindPlacement(render)));
            ShowRender(result.Figure);
            Page.Select(result.Select?.FirstOrDefault());
            await ReadState(render.Figure);
            receive?.Invoke(result);
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
        Geometry.Header = page ? $"Page geometry ({geometryUnit})" : panel ? $"Layer geometry ({geometryUnit})" : count == 1 ? "Layer" : $"{count} selected layers";
        XField.Visibility = YField.Visibility = panel ? Visibility.Visible : Visibility.Collapsed;
        GeometryInputs.Visibility = ApplyButton.Visibility = page || panel ? Visibility.Visible : Visibility.Collapsed;
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
        XInput.Text = panel ? GeometryNumber(selected!.Box![0]) : "";
        YInput.Text = panel ? GeometryNumber(selected!.Box![1]) : "";
        WidthInput.Text = GeometryNumber(panel ? selected!.Box![2] : page ? currentRender?.Size?[0] : null);
        HeightInput.Text = GeometryNumber(panel ? selected!.Box![3] : page ? currentRender?.Size?[1] : null);
        System.Windows.Automation.AutomationProperties.SetName(XInput, $"X ({geometryUnit})");
        System.Windows.Automation.AutomationProperties.SetName(YInput, $"Y ({geometryUnit})");
        System.Windows.Automation.AutomationProperties.SetName(WidthInput, $"Width ({geometryUnit})");
        System.Windows.Automation.AutomationProperties.SetName(HeightInput, $"Height ({geometryUnit})");
        UpdateInspector();
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
                Status.Text = i >= 2 ? $"Width and height must be positive numbers ({geometryUnit})." : $"X and Y must be numbers ({geometryUnit}).";
                field.Focus();
                return Task.CompletedTask;
            }
            value *= MillimetersPerUnit;
            if (field.Text.Trim() != GeometryNumber(current[i]) && Math.Abs(value - current[i]) > 0.00001) props.Add(key, value);
        }
        if (props.Count == 0) return Task.CompletedTask;
        object args = panel switch
        {
            null => new { figure, op = "figure", props },
            { Kind: "qr" } => new { figure, op = "set", oid = panel.Oid,
                props = props.Where(p => p.Key != "h").ToDictionary() },
            { Kind: "inset" or "figure" or "zoom" or "path" } => new { figure, op = "resize", oid = panel.Oid, box = new[]
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

    private sealed record ProblemRow(string Badge, string Message, string Tint, string? Oid = null)
    {
        public string? Hint => Oid is not null ? "Select the affected layer" : null;
    }
    private static ProblemRow[] PreflightRows(FigureWarning[]? warnings) => (warnings ?? []).Select(w => w.Level switch
    {
        "error" => new ProblemRow("Error", w.Message, "#AA2020", w.Oid),
        "info" => new ProblemRow("Info", w.Message, "#546166", w.Oid),
        _ => new ProblemRow("Warning", w.Message, "#88520E", w.Oid)
    }).ToArray();

    private void ShowWarnings(FigureWarning[]? warnings)
    {
        int count = (warnings ?? []).Count(w => w.Level != "info");
        PreflightTab.Header = count > 0 ? $"Preflight ({count})" : "Preflight";
        var messages = PreflightRows(warnings);
        Warnings.ItemsSource = messages.Length > 0 ? messages : [new("OK", "No preflight messages.", "#246B6B")];
    }

    private void ShowExportMessages(IEnumerable<string> warnings, IEnumerable<string>? notes = null) => Warnings.ItemsSource =
        warnings.Distinct().Select(w => new ProblemRow("Export", w, "#88520E"))
            .Concat((notes ?? []).Distinct().Select(n => new ProblemRow("Export info", n, "#546166")))
            .Concat(PreflightRows(currentRender?.Warnings)).ToArray();

    private void WarningSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Warnings.SelectedItem is not ProblemRow { Oid: { } oid } || currentRender is not { } render) return;
        oid = render.Labels?.FirstOrDefault(l => l.Oid == oid)?.Panel ?? oid;
        if (render.Elements?.Any(l => l.Oid == oid) == true) Page.Select(oid);
    }

    internal static string FormatMm(double? value) => value?.ToString("0.####", CultureInfo.CurrentCulture) ?? "";

    internal static bool TryMm(string text, bool positive, out double value) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
         double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) &&
        double.IsFinite(value) && (!positive || value > 0);
}
