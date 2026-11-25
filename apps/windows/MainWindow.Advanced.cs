using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private Task MakeLayout()
    {
        if (shownFigure is not { } figure || !CanMakeLayout()) return Task.CompletedTask;
        return EditFigure(figure, new { figure, op = "make_layout", oids = Page.SelectedIds, gap = 4 }, "Creating layout…", clearSelection: true);
    }

    private bool CanMakeLayout() => Page.SelectedIds.Count >= 2 && Page.Selections.All(e =>
        FigureCanvas.Movable(e) && e.Kind is "image" or "panel");

    private Task ReleaseLayout() => shownFigure is { } figure && Page.Selection is { Kind: "layout" } layout && FigureCanvas.Unlocked(layout)
        ? EditFigure(figure, new { figure, op = "release_layout", oid = layout.Oid }, "Releasing layout…", clearSelection: true)
        : Task.CompletedTask;

    private Task MoveDivider(string oid, int index, double delta) => shownFigure is not { } figure ? Task.CompletedTask :
        EditFigure(figure, new { figure, op = "divider", oid, index, delta }, "Resizing layout tracks…");

    private Task AddImageInset() => shownFigure is { } figure && Page.Selection is { Kind: "image" } image && FigureCanvas.Unlocked(image)
        ? EditFigure(figure, new { figure, op = "add_zoom", oids = new[] { image.Oid } }, "Adding image zoom inset…") : Task.CompletedTask;

    private Task ChooseScaleBar()
    {
        if (shownFigure is not { } figure || currentRender is not { } original ||
            Page.Selection is not { Kind: "image" } image || !FigureCanvas.Unlocked(image)) return Task.CompletedTask;
        var dialog = new FormDialog(this, "Add scale bar");
        dialog.Note("Enter the actual calibration from the original image, not the figure’s display dimensions.");
        var unit = dialog.Text("Unit", "µm");
        var pixel = dialog.Text("Units per original image pixel", "");
        var length = dialog.Text("Scale bar length in those units", "");
        double px = 0, len = 0;
        if (!dialog.Confirm("Add", () => string.IsNullOrWhiteSpace(unit.Text) || !TryMm(pixel.Text, true, out px) ||
             !TryMm(length.Text, true, out len) ? "Enter a unit and positive calibration and length." : null) ||
             !StillEditing(original)) return Task.CompletedTask;
        return AddScaleBar(figure, image.Oid, len, px, unit.Text.Trim());
    }

    private Task AddScaleBar(string figure, string oid, double length, double pixelSize, string unit) => EditFigure(figure,
        new { figure, op = "add_scalebar", oid, props = new Dictionary<string, object>
            { ["length"] = length, ["unit"] = unit, ["pixel_size"] = pixelSize } }, "Adding calibrated scale bar…");

    private Task EditStyle(bool labels)
    {
        if (shownFigure is not { } figure || currentRender is not { } original ||
            (labels ? original.LabelStyle : original.Style) is not { } style) return Task.CompletedTask;
        var dialog = new FormDialog(this, labels ? "Panel lettering style" : "Plot style");
        if (!labels) dialog.Note("When the style comes from a shared Python file, other figures using it will also change.");
        string StringValue(string key, string fallback) => style.Props.TryGetValue(key, out var x) && x.ValueKind == JsonValueKind.String
            ? x.GetString()! : fallback;
        double NumberValue(string key, double fallback) => style.Props.TryGetValue(key, out var x) && x.ValueKind == JsonValueKind.Number
            ? x.GetDouble() : fallback;
        var numeric = new Dictionary<string, (TextBox Text, double Start, bool Positive)>();
        void Number(string key, string title, double fallback, bool positive = true)
        {
            double value = NumberValue(key, fallback);
            numeric[key] = (dialog.Text(title, FormatMm(value)), value, positive);
        }
        string font = StringValue("font", "");
        var fontBox = dialog.Text(labels ? "Font (blank inherits the plot font)" : "Font family", font);
        Number("size", "Text size (pt)", labels ? 8 : 7);
        ComboBox? numbering = null, casing = null;
        string pattern = "{}", numberingStart = "alpha", caseStart = "lower";
        TextBox? format = null;
        if (labels)
        {
            numberingStart = StringValue("numbering", "alpha");
            caseStart = StringValue("case", "lower");
            string[] numberings = ["alpha", "arabic", "roman", "none"];
            numbering = dialog.Choice("Numbering", numberings.Contains(numberingStart) ? numberings : [.. numberings, numberingStart], numberingStart);
            casing = dialog.Choice("Case", new[] { "lower", "upper" }, caseStart);
            pattern = StringValue("format", "{}");
            format = dialog.Text("Format ({} is replaced by the label)", pattern);
            Number("dx", "Offset X (mm)", 0, false);
            Number("dy", "Offset Y (mm)", 0, false);
        }
        else
        {
            Number("linewidth", "Line width (pt)", 0.6);
            Number("min_font", "Minimum text size for preflight (pt)", 5);
        }
        var props = new Dictionary<string, object?>();
        if (!dialog.Confirm("Apply", () =>
        {
            props.Clear();
            string nextFont = fontBox.Text.Trim();
            if (!labels && nextFont.Length == 0) return "Enter a font family.";
            if (nextFont != font) props["font"] = nextFont.Length == 0 ? null : nextFont;
            foreach (var (key, (text, initial, positive)) in numeric)
            {
                if (!TryMm(text.Text, positive, out double value)) return $"Enter a valid value for {key}.";
                if (text.Text != FormatMm(initial) && Math.Abs(value - initial) > 0.00001) props[key] = value;
            }
            if (numbering?.SelectedItem is string n && n != numberingStart) props["numbering"] = n;
            if (casing?.SelectedItem is string c && c != caseStart) props["case"] = c;
            if (format is not null && format.Text != pattern)
            {
                string remainder = format.Text.Replace("{}", "");
                if (remainder.Contains('{') || remainder.Contains('}') || remainder.Length + 2 != format.Text.Length)
                    return "Use one {} placeholder, for example ({}).";
                props["format"] = format.Text;
            }
            return null;
        }) || props.Count == 0 || !StillEditing(original)) return Task.CompletedTask;
        return ApplyStyle(figure, labels ? "labels" : "style", props);
    }

    private Task ApplyStyle(string figure, string op, Dictionary<string, object?> props) => EditFigure(figure,
        new { figure, op, props }, "Updating style…");
}
