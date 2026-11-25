using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private JournalChoice[]? journals;
    private static readonly JournalChoice GeneralJournal = new("", "General (no journal preset)");
    private static readonly JournalChoice FigureJournal = new("", "Use figure’s journal / default");
    private static readonly string[] AnnotationKinds = ["Text", "Arrow", "Significance bracket"];

    private async Task<JournalChoice[]> Journals()
    {
        if (journals is not null) return journals;
        var entries = await GetEngine().CallAsync<Dictionary<string, JournalPreset>>("journals");
        return journals = entries.Select(p => new JournalChoice(p.Key, p.Value.Name, p.Value.Widths)).OrderBy(j => j.Name).ToArray();
    }

    private async void AnnotationClicked(object sender, RoutedEventArgs e)
    {
        if (currentRender?.Size is not [var width, var height] || shownFigure is not { } figure) return;
        var dialog = new FormDialog(this, "Add annotation");
        var kind = dialog.Choice("Type", AnnotationKinds, AnnotationKinds[0]);
        var text = dialog.Text("Text or significance label (not used for arrows)", "");
        var x = dialog.Text("X (mm)", FormatMm(Math.Min(10, width / 4)));
        var y = dialog.Text("Y (mm)", FormatMm(Math.Min(10, height / 4)));
        double px = 0, py = 0;
        if (!dialog.Confirm("Add", () =>
            !TryMm(x.Text, false, out px) || !TryMm(y.Text, false, out py) || px < 0 || py < 0 || px >= width || py >= height
                ? "Enter a position inside the page." : null)) return;
        await InsertAnnotation(figure, kind.SelectedIndex, text.Text.Trim(), px, py, Math.Min(20, (width - px) * 0.8));
    }

    private Task InsertAnnotation(string figure, int type, string text, double x, double y, double span)
    {
        string kind = type switch { 1 => "arrow", 2 => "bracket", _ => "text" };
        object props = type switch
        {
            1 => new { x1 = x, y1 = y, x2 = Math.Round(x + span, 2), y2 = y },
            2 => new { x1 = x, x2 = Math.Round(x + span, 2), y, text = text.Length == 0 ? "*" : text },
            _ => new { x, y, text = text.Length == 0 ? "Text" : text }
        };
        return EditFigure(figure, new { figure, op = "insert", kind, props }, "Adding annotation…");
    }

    private async void DetailsClicked(object sender, RoutedEventArgs e) => await EditDetails();

    private Task SetDetails(string figure, string oid, Dictionary<string, object?> props) =>
        EditFigure(figure, new { figure, op = "set", oid, props }, "Updating layer…");

    private async Task EditDetails()
    {
        if (shownFigure is not { } figure || Page.Selection is not { } layer || !FigureCanvas.Unlocked(layer)) return;
        if (layer.Kind is "panel" or "image")
        {
            var dialog = new FormDialog(this, "Panel label");
            string? label = layer.Props?.Label;
            string mode = label is null ? "None" : label == "auto" ? "Automatic" : "Custom";
            var choice = dialog.Choice("Lettering", new[] { "Automatic", "Custom", "None" }, mode);
            var text = dialog.Text("Custom label", mode == "Custom" ? label! : "");
            if (dialog.Confirm("Apply", () => choice.SelectedIndex == 1 && string.IsNullOrWhiteSpace(text.Text) ? "Enter a custom label." : null))
                await SetDetails(figure, layer.Oid, new() { ["label"] = choice.SelectedIndex switch { 1 => text.Text.Trim(), 2 => null, _ => "auto" } });
        }
        else if (layer.Kind is "text" or "bracket" or "line")
        {
            var dialog = new FormDialog(this, $"Edit {layer.Method ?? layer.Kind}");
            var inputs = new Dictionary<string, (TextBox Box, double Initial)>();
            void Number(string key, string name, double? value)
            {
                if (value is { } n) inputs[key] = (dialog.Text(name, FormatMm(n)), n);
            }
            TextBox? text = layer.Kind is "text" or "bracket" ? dialog.Text("Text", layer.Props?.Text ?? "", layer.Kind == "text") : null;
            Number("x1", "Start X (mm)", layer.Props?.X1);
            Number("y1", "Start Y (mm)", layer.Props?.Y1);
            Number("x2", "End X (mm)", layer.Props?.X2);
            Number("y2", "End Y (mm)", layer.Props?.Y2);
            var props = new Dictionary<string, object?>();
            if (dialog.Confirm("Apply", () =>
            {
                props.Clear();
                if (text is not null && text.Text != (layer.Props?.Text ?? "")) props["text"] = text.Text;
                foreach (var (key, (box, initial)) in inputs)
                {
                    if (!TryMm(box.Text, false, out double number)) return "Coordinates must be numbers in millimeters.";
                    if (box.Text != FormatMm(initial) && Math.Abs(number - initial) > 0.00001) props[key] = number;
                }
                return null;
            }) && props.Count > 0) await SetDetails(figure, layer.Oid, props);
        }
    }

    private async void PresetClicked(object sender, RoutedEventArgs e)
    {
        if (shownFigure is not { } figure) return;
        JournalChoice[] choices = [];
        await During("Loading journal widths…", async () => { choices = await Journals(); Status.Text = "Choose a journal column width."; });
        if (choices.Length == 0) return;
        var dialog = new FormDialog(this, "Journal column width");
        dialog.Note("Sets the page width and journal for preflight. The existing plot style is left alone.");
        var journal = dialog.Choice("Journal", choices, choices.FirstOrDefault(j => j.Key == currentRender?.Journal?.Key) ?? choices[0]);
        var widths = dialog.Choice("Column", Array.Empty<string>(), "");
        void Update() { widths.ItemsSource = ((JournalChoice)journal.SelectedItem).Widths!.Keys; widths.SelectedIndex = 0; }
        journal.SelectionChanged += (_, _) => Update();
        Update();
        if (dialog.Confirm("Apply", () => widths.SelectedItem is null ? "Choose a column." : null))
        {
            var selected = (JournalChoice)journal.SelectedItem;
            string column = (string)widths.SelectedItem;
            await ApplyPreset(figure, selected.Key, column, selected.Widths![column]);
        }
    }

    private Task ApplyPreset(string figure, string journal, string columns, double width) =>
        EditFigure(figure, new { figure, op = "figure", props = new { journal, columns, width } }, "Applying journal width…");

    private async Task ChooseExport(string figure)
    {
        JournalChoice[] choices = [];
        await During("Loading export options…", async () => { choices = await Journals(); Status.Text = "Choose export options."; });
        if (choices.Length == 0) return;
        var dialog = new FormDialog(this, "Export figure");
        var formats = new[] { "PDF", "SVG", "PNG", "TIFF", "PPTX" };
        var format = dialog.Choice("Format", formats, formats[0]);
        var journal = dialog.Choice("Journal resolution rules", choices.Prepend(FigureJournal), FigureJournal);
        var resolution = dialog.Text("Resolution (dpi; raster content only)", "600");
        int dpi = 600;
        if (!dialog.Confirm("Continue", () => int.TryParse(resolution.Text, NumberStyles.None, CultureInfo.InvariantCulture, out dpi) &&
            dpi is >= 72 and <= 2400 ? null : "Enter a whole-number resolution between 72 and 2400 dpi.")) return;
        string ext = ((string)format.SelectedItem).ToLowerInvariant();
        var save = new SaveFileDialog { Title = "Export figure", Filter = $"{format.SelectedItem} (*.{ext})|*.{ext}",
            DefaultExt = "." + ext, FileName = figure, InitialDirectory = project };
        if (save.ShowDialog(this) == true)
        {
            if (!Path.GetExtension(save.FileName).Equals("." + ext, StringComparison.OrdinalIgnoreCase))
            { Status.Text = $"Use the .{ext} filename extension for this format."; return; }
            await ExportFigure(figure, save.FileName, dpi, ((JournalChoice)journal.SelectedItem).Key);
        }
    }
}
