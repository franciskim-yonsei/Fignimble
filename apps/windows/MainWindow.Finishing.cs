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
    private static readonly string[] AnnotationKinds = ["Text", "Arrow", "Significance bracket", "Line", "Rectangle", "Ellipse", "QR code"];

    private async Task<JournalChoice[]> Journals()
    {
        if (journals is not null) return journals;
        using var temporary = engine is { Running: true } ? null : new EngineClient(ProjectPython(null));
        var client = temporary ?? engine!;
        var entries = await client.CallAsync<Dictionary<string, JournalPreset>>("journals");
        return journals = entries.Select(p => new JournalChoice(p.Key, p.Value.Name, p.Value.Widths, p.Value.Dpi, p.Value.Labels)).OrderBy(j => j.Name).ToArray();
    }

    private async void AnnotationClicked(object sender, RoutedEventArgs e)
    {
        if (currentRender?.Size is not [var width, var height] || shownFigure is not { } figure) return;
        var dialog = new FormDialog(this, "Add annotation");
        var kind = dialog.Choice("Type", AnnotationKinds, AnnotationKinds[0]);
        var text = dialog.Text("Text, significance label, or QR content (as applicable)", "", multiline: true);
        var x = dialog.Text("X (mm)", FormatMm(Math.Min(10, width / 4)));
        var y = dialog.Text("Y (mm)", FormatMm(Math.Min(10, height / 4)));
        double px = 0, py = 0;
        if (!dialog.Confirm("Add", () =>
            !TryMm(x.Text, false, out px) || !TryMm(y.Text, false, out py) || px < 0 || py < 0 || px >= width || py >= height
                ? "Enter a position inside the page." : kind.SelectedIndex == 6 && string.IsNullOrWhiteSpace(text.Text) ? "Enter QR content." : null)) return;
        await InsertAnnotation(figure, kind.SelectedIndex, text.Text.Trim(), px, py, Math.Min(20, (width - px) * 0.8));
    }

    private Task InsertAnnotation(string figure, int type, string text, double x, double y, double span)
    {
        string kind = type switch { 1 => "arrow", 2 => "bracket", 3 => "line", 4 => "rect", 5 => "ellipse", 6 => "qr", _ => "text" };
        object props = type switch
        {
            1 or 3 => new { x1 = x, y1 = y, x2 = Math.Round(x + span, 2), y2 = y },
            2 => new { x1 = x, x2 = Math.Round(x + span, 2), y, text = text.Length == 0 ? "*" : text },
            4 or 5 => new { x, y, w = span, h = span / 2 },
            6 => new { x, y, w = span, data = text },
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
            string? label = layer.Props?.LabelText;
            string mode = label is null ? "None" : label == "auto" ? "Automatic" : "Custom";
            var choice = dialog.Choice("Lettering", new[] { "Automatic", "Custom", "None" }, mode);
            var text = dialog.Text("Custom label", mode == "Custom" ? label! : "");
            if (dialog.Confirm("Apply", () => choice.SelectedIndex == 1 && string.IsNullOrWhiteSpace(text.Text) ? "Enter a custom label." : null))
                await SetDetails(figure, layer.Oid, new() { ["label"] = choice.SelectedIndex switch { 1 => text.Text.Trim(), 2 => null, _ => "auto" } });
        }
        else if (layer.Kind is "text" or "bracket" or "line" or "qr")
        {
            var dialog = new FormDialog(this, $"Edit {layer.Method ?? layer.Kind}");
            var inputs = new Dictionary<string, (TextBox Box, double Initial)>();
            void Number(string key, string name, double? value)
            {
                if (value is { } n) inputs[key] = (dialog.Text(name, FormatMm(n)), n);
            }
            string initialText = (layer.Kind == "qr" ? layer.Props?.DataText : layer.Props?.Text) ?? "";
            TextBox? text = layer.Kind is "text" or "bracket" or "qr" ? dialog.Text(layer.Kind == "qr" ? "Contents" : "Text", initialText, layer.Kind == "text") : null;
            if (layer.Kind == "text")
            {
                Number("size", "Type size (pt)", layer.Props?.Size);
                Number("width", "Text box width (mm)", layer.Props?.Width);
                Number("height", "Text box height (mm)", layer.Props?.Height);
            }
            Number("x1", "Start X (mm)", layer.Props?.X1);
            Number("y1", "Start Y (mm)", layer.Props?.Y1);
            Number("x2", "End X (mm)", layer.Props?.X2);
            Number("y2", "End Y (mm)", layer.Props?.Y2);
            var props = new Dictionary<string, object?>();
            if (dialog.Confirm("Apply", () =>
            {
                props.Clear();
                if (text is not null && text.Text != initialText) props[layer.Kind == "qr" ? "data" : "text"] = text.Text.Replace("\r\n", "\n");
                foreach (var (key, (box, initial)) in inputs)
                {
                    if (!TryMm(box.Text, key is "size" or "width" or "height", out double number)) return "Enter valid coordinates or positive dimensions.";
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
        string[] formats = ["PDF", "SVG", "PNG", "TIFF", "JPEG", "EPS", "PPTX"];
        var table = StandaloneTable;
        if (table is not null) formats = [.. formats, "DOCX", "XLSX", "CSV", "TSV"];
        bool poster = currentRender?.Poster is not null;
        bool pictures = currentRender?.Elements?.Any(e => e.Kind == "image" &&
            System.IO.Path.GetExtension(e.Props?.Source)?.ToLowerInvariant() is not (".pdf" or ".svg")) == true;
        var format = dialog.Choice("Format", formats, formats[0]);
        var general = new JournalChoice("general", "General (300 / 600 / 1200 dpi)");
        var journal = dialog.Choice("Journal resolution rules", new[] { general, FigureJournal }.Concat(choices), general);
        string[] dpiPresets = ["300", "500", "600", "1000", "1200", "2400"];
        var resolution = dialog.Choice("Resolution (dpi; raster content only)", dpiPresets, "600");
        resolution.IsEditable = true;
        var useDark = dialog.Check("Export the figure’s dark version", darkPreview);
        var cmyk = dialog.Check("CMYK");
        var outline = dialog.Check("Convert text to outlines");
        var marks = dialog.Check("Crop marks (PDF)");
        var bleed = dialog.Text("Bleed (mm, PDF)", poster ? "3" : "0");
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        dialog.Add("", new ContentControl { Content = hint });
        dialog.Note("EPS and CMYK or outlined PDF require Ghostscript on the rendering machine. Confirm the print shop’s preferred color profile separately.");
        void Options()
        {
            string? kind = format.SelectedItem as string;
            bool editableTable = kind is "DOCX" or "XLSX" or "CSV" or "TSV";
            bool raster = kind is "PNG" or "TIFF" or "JPEG";
            cmyk.IsEnabled = kind is "PDF" or "EPS" or "TIFF" or "JPEG";
            outline.IsEnabled = kind is "PDF" or "EPS" or "SVG";
            marks.IsEnabled = bleed.IsEnabled = kind == "PDF";
            journal.IsEnabled = resolution.IsEnabled = useDark.IsEnabled = !editableTable;
            var selected = journal.SelectedItem as JournalChoice;
            if (selected?.Key == "") selected = choices.FirstOrDefault(j => j.Key == currentRender?.Journal?.Key);
            bool posterRules = poster && (selected is null || selected.Key == "general");
            int photos = selected?.Dpi?.GetValueOrDefault("photo", 300) ?? 300;
            int combination = selected?.Dpi?.GetValueOrDefault("combination", 600) ?? 600;
            int line = selected?.Dpi?.GetValueOrDefault("line", 1200) ?? 1200;
            int suggested = raster ? posterRules ? 300 : pictures ? combination : line : kind == "PPTX" ? posterRules ? 200 : 300 : 600;
            resolution.Text = suggested.ToString(CultureInfo.InvariantCulture);
            hint.Text = editableTable ? "Exports editable cells rather than a picture."
                : kind == "PPTX" ? "PowerPoint contains a vector copy and a PNG fallback at the chosen resolution."
                : posterRules ? "Posters use 150–300 dpi at full size. PDF stays vector; raster output is capped at 300 dpi."
                : raster ? $"{line} dpi for line art, {combination} dpi for figures with pictures. TIFF and JPEG may be raised to meet the required resolution."
                : $"Vector paths stay sharp. The export checks raster images against the {photos} dpi photo requirement.";
        }
        format.SelectionChanged += (_, _) => Options();
        journal.SelectionChanged += (_, _) => Options();
        Options();
        int dpi = 600;
        double edge = 0;
        if (!dialog.Confirm("Continue", () =>
        {
            if (resolution.IsEnabled && (!int.TryParse(resolution.Text, NumberStyles.None, CultureInfo.InvariantCulture, out dpi) || dpi is < 72 or > 2400))
                return "Enter a whole-number resolution between 72 and 2400 dpi.";
            if (bleed.IsEnabled && (!TryMm(bleed.Text, false, out edge) || edge < 0 || edge > 100)) return "Enter bleed between 0 and 100 mm.";
            return null;
        })) return;
        string ext = ((string)format.SelectedItem).ToLowerInvariant();
        var save = new SaveFileDialog { Title = "Export figure", Filter = $"{format.SelectedItem} (*.{ext})|*.{ext}",
            DefaultExt = "." + ext, FileName = figure, InitialDirectory = project };
        if (save.ShowDialog(this) == true)
        {
            if (!Path.GetExtension(save.FileName).Equals("." + ext, StringComparison.OrdinalIgnoreCase))
            { Status.Text = $"Use the .{ext} filename extension for this format."; return; }
            if (table is not null && ext is "docx" or "xlsx" or "csv" or "tsv")
            { await ExportTable(figure, table.Oid, save.FileName); return; }
            await ExportWithOptions(figure, save.FileName, dpi, ((JournalChoice)journal.SelectedItem).Key,
                useDark.IsChecked == true, cmyk.IsEnabled && cmyk.IsChecked == true, outline.IsEnabled && outline.IsChecked == true,
                bleed.IsEnabled ? edge : 0, marks.IsEnabled && marks.IsChecked == true);
        }
    }
}
