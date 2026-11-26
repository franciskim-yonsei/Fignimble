using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private FigureRender? propertiesRender;
    private string? propertiesOid;
    private int propertiesCount;
    private (string Oid, string Key, bool Text)? propertiesPart;
    private Func<string, Dictionary<string, object?>, Task>? applyPartProperties;
    private readonly List<Func<Dictionary<string, object?>, string?>> propertyInputs = [];

    private void RefreshProperties(FigureElement? layer)
    {
        PropertyEditor.IsEnabled = !busy;
        if (ReferenceEquals(propertiesRender, currentRender) && propertiesOid == layer?.Oid && propertiesCount == Page.SelectedIds.Count && propertiesPart == Page.PanelPart) return;
        propertiesPart = Page.PanelPart;
        applyPartProperties = null;
        propertiesCount = Page.SelectedIds.Count;
        propertiesRender = currentRender;
        propertiesOid = layer?.Oid;
        PropertyFields.Children.Clear();
        propertyInputs.Clear();
        PropertyEditor.Visibility = Visibility.Collapsed;
        if (!validFigure || layer is not null && !FigureCanvas.Unlocked(layer)) return;
        if (layer?.Kind == "legend" && layer.Parent is not null) return;
        var p = layer?.Props;
        JsonElement Value(string key) => p?.Extra?.GetValueOrDefault(key) ?? default;
        string ValueText(string key, string fallback = "") => Value(key) is var v && v.ValueKind switch
        {
            JsonValueKind.String => true, JsonValueKind.Number => true, _ => false
        } ? v.ToString() : fallback;
        void Add(string label, Control control)
        {
            var text = new Label { Content = label, Target = control, Padding = new Thickness(0, 5, 0, 2) };
            PropertyFields.Children.Add(text);
            PropertyFields.Children.Add(control);
        }
        void Field(string key, string label, string original, Func<string, (bool Ok, object? Value)> parse)
        {
            var box = new TextBox { Text = original, Padding = new Thickness(4, 2, 4, 2) };
            Add(label, box);
            propertyInputs.Add(props =>
            {
                if (box.Text == original) return null;
                var (ok, value) = parse(box.Text.Trim());
                if (!ok) { box.Focus(); return $"Enter a valid value for {label.ToLowerInvariant()}."; }
                props[key] = value;
                return null;
            });
        }
        void Text(string key, string label, string? initial = null, bool optional = false) =>
            Field(key, label, initial ?? ValueText(key), s => (optional || s.Length > 0, optional && s.Length == 0 ? null : s));
        void Numeric(string key, string label, double? initial = null, double min = double.NegativeInfinity,
            double max = double.PositiveInfinity, bool optional = false, bool integer = false, double factor = 1)
        {
            if (initial is null && Value(key) is { ValueKind: JsonValueKind.Number } value) initial = value.GetDouble();
            Field(key, label, FormatMm(initial * factor), s =>
            {
                if (optional && s.Length == 0) return (true, null);
                if (!TryMm(s, false, out double n) || n < min || n > max || integer && n != Math.Truncate(n)) return (false, null);
                return (true, integer ? (object)(int)n : n / factor);
            });
        }
        void Choice(string key, string label, string[] values, string fallback = "", bool optional = false)
        {
            string original = ValueText(key, fallback);
            string[] choices = values.Contains(original) ? values : [.. values, original];
            const string inherit = "(default / none)";
            var box = new ComboBox { ItemsSource = choices.Select(s => s == "" ? inherit : s).ToArray(),
                SelectedItem = original == "" ? inherit : original };
            Add(label, box);
            propertyInputs.Add(props =>
            {
                var value = box.SelectedItem as string;
                if (value == inherit) value = "";
                if (value != original) props[key] = optional && value == "" ? null : value;
                return null;
            });
        }
        void Toggle(string key, string label, bool fallback = false, string? yes = null)
        {
            bool original = yes is not null ? ValueText(key) == yes : Value(key).ValueKind switch
            { JsonValueKind.True => true, JsonValueKind.False => false, _ => fallback };
            var box = new CheckBox { Content = label, IsChecked = original, Margin = new Thickness(0, 5, 0, 3) };
            PropertyFields.Children.Add(box);
            propertyInputs.Add(props =>
            {
                bool on = box.IsChecked == true;
                if (on != original) props[key] = yes is not null ? on ? yes : null : on;
                return null;
            });
        }
        void Color(string key, string label, string? initial = null, bool optional = false)
        {
            Field(key, label, initial ?? ValueText(key), s =>
            {
                if (s.Length == 0) return (optional, null);
                // Fignimble also accepts other matplotlib color syntax in authored code.
                bool hex = s.StartsWith('#') && s.Length is 4 or 7 or 9 && s.Skip(1).All(Uri.IsHexDigit);
                bool named = s.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or ':');
                return (hex || named, s);
            });
        }
        void PositiveList(string key, string label)
        {
            string start = Value(key).ValueKind == JsonValueKind.Array ? string.Join(" ", Value(key).EnumerateArray().Select(v => v.GetRawText())) : "";
            Field(key, label, start, s =>
            {
                if (s == "") return (true, null);
                var items = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var result = new double[items.Length];
                for (int i = 0; i < items.Length; i++)
                    if (!double.TryParse(items[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]) ||
                        !double.IsFinite(result[i]) || result[i] <= 0) return (false, null);
                return (true, result);
            });
        }
        void Button(string label, Func<Task> action)
        {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
            button.Click += async (_, _) => await action();
            PropertyFields.Children.Add(button);
        }
        void LineLook(bool stroke)
        {
            if (stroke) Color("stroke", "Stroke (blank for none)", optional: true);
            else Color("color", "Color");
            Numeric("width", "Stroke width (pt)", p?.Width, 0);
            Choice("dash", "Dash", ["", "dashed", "dotted"], optional: true);
            Numeric("opacity", "Opacity (0–1)", min: 0, max: 1);
        }
        void ArrowLook()
        {
            Choice("arrow", "Arrow ends", ["", "start", "end", "both"], optional: true);
            Choice("tip", "Arrowhead", ["triangle", "open", "stealth", "bar", "dot"], "triangle");
            Numeric("head", "Arrowhead length (mm)", min: 0.01);
        }
        void Typography()
        {
            Text("font", "Font (blank inherits)", optional: true);
            Numeric("size", "Text size (pt, blank inherits)", p?.Size, 0.01, optional: true);
            Color("color", "Text color");
        }

        var part = Page.PanelPart;
        if (layer is not null && part is { Text: true } && layer.Texts?.FirstOrDefault(t => t.Key == part.Value.Key) is { } text)
        {
            Button("Retype plot text", () => EditClickedPlotText(layer, text));
            Numeric("size", "Text size (pt)", text.Size, 0.01);
            Color("color", "Text color", text.Color);
            Choice("weight", "Weight", ["normal", "bold"], text.Bold ? "bold" : "normal");
            Choice("style", "Style", ["normal", "italic"], text.Italic ? "italic" : "normal");
            applyPartProperties = (figure, props) => EditFigure(figure, new { figure, op = "panel_text_style", oid = layer.Oid, key = text.Key, text = text.Text, props }, "Updating plot text style…");
            if (PanelOverrideMap(layer, "texts").ContainsKey(text.Key) || PanelOverrideMap(layer, "text_style").ContainsKey(text.Key))
                Button("Restore text from code", () => shownFigure is { } f ? EditFigure(f, new { figure = f, op = "panel_text_reset", oid = layer.Oid, key = text.Key }, "Resetting plot text…") : Task.CompletedTask);
        }
        else if (layer is not null && part is { Text: false } && layer.Marks?.FirstOrDefault(m => m.Key == part.Value.Key) is { } mark)
        {
            Color("color", "Mark color", mark.Color);
            applyPartProperties = (figure, props) => RecolorMarks(figure, layer, [mark.Key], mark.Color, (string)props["color"]!);
            var overrides = PanelOverrideMap(layer, "colors");
            if (overrides.Remove(mark.Key)) Button("Restore this mark’s color from code", () => shownFigure is { } f
                ? SetDetails(f, layer.Oid, new() { ["colors"] = overrides.Count == 0 ? null : overrides }) : Task.CompletedTask);
        }
        else if (propertiesCount > 1)
        {
            if (CanMakeLayout()) Button("Make Layout", MakeLayout);
            if (CanEditLayers("group")) Button("Group", () => EditLayers("group"));
            if (CanEditLayers("ungroup")) Button("Ungroup", () => EditLayers("ungroup"));
        }
        else switch (layer?.Kind)
        {
            case null:
                Color("background", "Page background (blank for transparent)", currentRender?.Background ?? "", optional: true);
                if (currentRender?.Poster is not null) Button("Poster page settings…", PosterSettings);
                break;
            case "layout":
                Numeric("gap", "Panel gap (mm)", min: 0);
                PositiveList("widths", "Column weights (space-separated)");
                PositiveList("heights", "Row weights (space-separated)");
                Toggle("guides", "Collect legends", yes: "collect");
                Choice("legend", "Legend position", ["right", "bottom"], "right");
                Toggle("align", "Align plot areas", true);
                break;
            case "inset":
                Numeric("x", "Left (% of host)", factor: 100);
                Numeric("y", "Top (% of host)", factor: 100);
                Numeric("w", "Width (% of host)", min: 0.01, factor: 100);
                Numeric("h", "Height (% of host)", p?.H, 0.01, factor: 100);
                if (layer.Inner == "image")
                {
                    string fit = layer.Item?.TryGetValue("fit", out var current) == true ? current.ToString() : "contain";
                    Field("fit", "Fit (contain / cover / stretch)", fit, s => (s is "contain" or "cover" or "stretch", s));
                }
                if (layer.Inner == "panel" && layer.Item?.TryGetValue("function", out var function) == true)
                    Text("function", "Drawing function", function.ToString());
                break;
            case "label":
                Text("text", "Panel label (blank restores automatic)", layer.Text, optional: true);
                Button(layer.Detached ? "Link to panel" : "Unlink from panel", ToggleLabelLink);
                break;
            case "panel":
                Text("function", "Drawing function");
                if (layer.Marks is { Length: > 0 }) Button("Recolor matching marks…", () => ChoosePanelColors(layer));
                break;
            case "image":
                Choice("fit", "Picture fit", ["contain", "cover", "stretch"], "contain");
                Numeric("opacity", "Opacity (0–1)", min: 0, max: 1);
                Numeric("rotation", "Rotation (° counterclockwise)", p?.Rotation);
                Button("Replace picture…", ReplacePicture);
                break;
            case "figure":
                Toggle("letters", "Show nested panel letters", true);
                Color("background", "Nested background (figure or color)", optional: true);
                Button("Open source figure", () =>
                {
                    string? name = System.IO.Path.GetFileNameWithoutExtension(layer.Props?.Source);
                    if (figureCards.FirstOrDefault(card => card.Name == name) is { } match) Figures.SelectedItem = match;
                    return Task.CompletedTask;
                });
                Button("Use natural size", () => shownFigure is { } f ? SetDetails(f, layer.Oid, new() { ["w"] = null, ["h"] = null }) : Task.CompletedTask);
                break;
            case "text":
                Typography();
                Choice("weight", "Weight", ["normal", "bold"], "normal");
                Choice("style", "Style", ["normal", "italic"], "normal");
                Choice("align", "Alignment", ["left", "center", "right"], p?.Align ?? "left");
                Numeric("rotation", "Rotation (°)", p?.Rotation);
                Numeric("opacity", "Opacity (0–1)", min: 0, max: 1);
                Numeric("width", "Wrap width (mm)", p?.Width, 0.01, optional: true);
                Numeric("height", "Box height (mm)", p?.Height, 0.01, optional: true);
                Choice("fit", "Overflow", ["", "shrink"], optional: true);
                if (p?.Source is not null) Button("Edit linked text", EditSource);
                break;
            case "line":
                LineLook(false); ArrowLook(); Numeric("curve", "Bend", min: -2, max: 2);
                break;
            case "path":
                LineLook(true); ArrowLook(); Color("fill", "Fill (blank for none)", optional: true);
                Toggle("closed", "Closed path"); Toggle("smooth", "Smooth path");
                break;
            case "rect": case "ellipse":
                LineLook(true); Color("fill", "Fill (blank for none)", optional: true);
                if (layer.Kind == "rect") Numeric("radius", "Corner radius (mm)", min: 0);
                break;
            case "bracket":
                Text("text", "Bracket label", p?.Text);
                Numeric("tick", "Tick length (mm)", min: 0);
                Numeric("size", "Label size (pt)", p?.Size, 0.01, optional: true);
                Numeric("width", "Stroke width (pt)", p?.Width, 0);
                Color("color", "Color");
                break;
            case "qr":
                Choice("level", "Error correction", ["L", "M", "Q", "H"], "M");
                Numeric("quiet", "Quiet zone (modules)", min: 0, max: 100, integer: true);
                Color("color", "Foreground"); Color("background", "Background", optional: true);
                break;
            case "table":
                Toggle("header", "First row is header", true);
                Toggle("grid", "Draw full table grid");
                Typography();
                var decimals = Value("decimals");
                string initial = decimals.ValueKind == JsonValueKind.Array ? string.Join(" ", decimals.EnumerateArray().Select(v =>
                    v.ValueKind == JsonValueKind.Null ? "-" : v.GetRawText())) : ValueText("decimals");
                Field("decimals", "Decimal places (or per-column: 0 - 2)", initial, s =>
                {
                    if (s == "") return (true, null);
                    var list = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    var places = new int?[list.Length];
                    for (int i = 0; i < list.Length; i++)
                    {
                        if (list[i] == "-") continue;
                        if (!int.TryParse(list[i], out int n) || n is < 0 or > 15) return (false, null);
                        places[i] = n;
                    }
                    return (true, places.Length == 1 ? (object?)places[0] : places);
                });
                Field("align", "Column alignment (e.g. lrr)", p?.Align ?? "", s =>
                    (s.All(c => c is 'l' or 'c' or 'r'), s.Length == 0 ? null : s));
                if (StandaloneTable is not null) Button("Fit page to table", () => shownFigure is { } f
                    ? EditFigure(f, new { figure = f, op = "fit_figure", margin = 3 }, "Fitting page…") : Task.CompletedTask);
                break;
            case "scalebar":
                Numeric("length", "Length", min: 0.000001); Text("unit", "Unit");
                Numeric("pixel_size", "Units per original pixel", min: 0.000001);
                Choice("corner", "Corner", ["lower right", "lower left", "upper right", "upper left"], "lower right");
                Numeric("margin", "Margin (mm)", min: 0); Numeric("thickness", "Thickness (mm)", min: 0.01);
                Color("color", "Color"); Toggle("label", "Show scale text", p?.Label.ValueKind != JsonValueKind.False);
                break;
            case "zoom":
                Toggle("lines", "Show connectors", true); Toggle("sync", "Synchronize picture crop", true);
                Color("color", "Color"); Numeric("width", "Stroke width (pt)", p?.Width, 0);
                Choice("dash", "Dash", ["", "dashed", "dotted"], optional: true);
                break;
        }
        PropertyEditor.Visibility = PropertyFields.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ApplyPropertiesButton.Visibility = propertyInputs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ApplyPropertiesClicked(object sender, RoutedEventArgs e) => await ApplyInspectorProperties();

    private Task ApplyInspectorProperties()
    {
        if (busy || !validFigure || shownFigure is not { } figure || !ReferenceEquals(propertiesRender, currentRender)) return Task.CompletedTask;
        var props = new Dictionary<string, object?>();
        foreach (var read in propertyInputs)
            if (read(props) is { } error) { Status.Text = error; return Task.CompletedTask; }
        if (props.Count == 0) return Task.CompletedTask;
        if (applyPartProperties is not null) return applyPartProperties(figure, props);
        return propertiesOid is { } oid ? SetDetails(figure, oid, props)
            : EditFigure(figure, new { figure, op = "figure", props }, "Updating page appearance…");
    }

    private static Dictionary<string, JsonElement> PanelOverrideMap(FigureElement layer, string key)
    {
        var source = layer.Kind == "inset" ? layer.Item : layer.Props?.Extra;
        return source?.TryGetValue(key, out var value) == true && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().ToDictionary(item => item.Name, item => item.Value) : [];
    }

    private Task ChoosePanelColors(FigureElement layer)
    {
        if (layer.Marks is not { Length: > 0 } marks || shownFigure is not { } figure) return Task.CompletedTask;
        var original = currentRender;
        var colors = marks.Select(m => m.Color).Distinct().Order().ToArray();
        var dialog = new FormDialog(this, "Recolor matching marks");
        var old = dialog.Choice("Existing color", colors, colors[0]);
        var next = dialog.Text("New hex color", colors[0]);
        old.SelectionChanged += (_, _) => next.Text = old.SelectedItem as string ?? "";
        if (!dialog.Confirm("Apply", () => HexColor(next.Text.Trim()) ? null : "Enter #RRGGBB.") || !StillEditing(original) || old.SelectedItem is not string color) return Task.CompletedTask;
        return RecolorMarks(figure, layer, marks.Where(m => m.Color == color).Select(m => m.Key).ToArray(), color, next.Text.Trim());
    }

    private Task RecolorMarks(string figure, FigureElement layer, string[] keys, string old, string color) =>
        EditFigure(figure, new { figure, op = "panel_color", oid = layer.Oid, keys, old, color }, "Recoloring plot marks…");

    private FigureElement? SelectedPanelLabel => Page.Selection is { } selected
        ? selected.Kind == "label" ? selected : currentRender?.Labels?.FirstOrDefault(l => l.Panel == selected.Oid)
        : null;

    private Task ToggleLabelLink() => !busy && shownFigure is { } figure && SelectedPanelLabel is { } label
        ? EditFigure(figure, new { figure, op = "label_detach", oid = label.Oid, detach = !label.Detached }, "Updating panel label link…")
        : Task.CompletedTask;

    private Task ReplacePicture()
    {
        if (busy || shownFigure is not { } figure || Page.Selection is not { Kind: "image" } el) return Task.CompletedTask;
        var picker = new OpenFileDialog { Title = "Replace picture", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.svg;*.pdf;*.tif;*.tiff;*.gif;*.bmp;*.webp" };
        return picker.ShowDialog(this) == true ? EditFigure(figure, new { figure, op = "replace_source", oid = el.Oid, path = picker.FileName }, "Replacing picture…")
            : Task.CompletedTask;
    }
}
