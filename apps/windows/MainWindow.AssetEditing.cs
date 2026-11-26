using System.Data;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private Task EditTableCells()
    {
        if (shownFigure is not { } figure || Page.Selection is not { Kind: "table", Props.Cells: { Length: > 0 } cells } layer ||
            !FigureCanvas.Unlocked(layer)) return Task.CompletedTask;
        string Cell(JsonElement value) => value.ValueKind switch
        { JsonValueKind.Null => "", JsonValueKind.String => value.GetString()!, _ => value.GetRawText() };
        int columns = cells.Max(row => row.Length);
        if (columns == 0) return Task.CompletedTask;
        var inputRender = currentRender;
        var data = new DataTable();
        for (int i = 0; i < columns; i++) data.Columns.Add($"Column {i + 1}", typeof(string));
        foreach (var row in cells) data.Rows.Add(row.Select(c => (object)Cell(c)).ToArray());
        var dialog = new FormDialog(this, "Edit table cells") { Width = 700, ResizeMode = ResizeMode.CanResize };
        dialog.Note("The grid includes the header row, when present. Apply saves all cells as one undo step.");
        var grid = new DataGrid { ItemsSource = data.DefaultView, Height = 300, CanUserAddRows = true, CanUserDeleteRows = true,
            CanUserSortColumns = false, CanUserReorderColumns = false, AutoGenerateColumns = true };
        dialog.Add("Cells (use the blank row to add one; Delete on a row header removes it)", grid);
        bool Commit() => grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true);
        void RefreshGrid() { grid.ItemsSource = null; grid.ItemsSource = data.DefaultView; }
        var add = new Button { Content = "Add column" };
        add.Click += (_, _) =>
        {
            if (!Commit()) return;
            data.Columns.Add($"Column {data.Columns.Count + 1}", typeof(string));
            RefreshGrid();
        };
        var remove = new Button { Content = "Remove last column" };
        remove.Click += (_, _) =>
        {
            if (data.Columns.Count <= 1 || !Commit()) return;
            data.Columns.RemoveAt(data.Columns.Count - 1);
            RefreshGrid();
        };
        dialog.Add("Columns", add);
        dialog.Add("", remove);
        if (!dialog.Confirm("Apply", () => !Commit() ? "Finish editing the selected cell." :
            data.Rows.Count == 0 ? "Keep at least one row." : null)) return Task.CompletedTask;
        string[][] rows = data.Rows.Cast<DataRow>().Select(r => r.ItemArray.Select(v => v?.ToString() ?? "").ToArray()).ToArray();
        return StillEditing(inputRender) ? ApplyTableCells(figure, layer.Oid, rows) : Task.CompletedTask;
    }

    private Task ApplyTableCells(string figure, string oid, string[][] rows) => EditFigure(figure,
        new { figure, op = "table_rows", oid, rows }, "Saving table cells…");

    private Task EditPlotText()
    {
        if (shownFigure is not { } figure || Page.Selection is not { Kind: "panel", Texts: { Length: > 0 } texts } layer ||
            !FigureCanvas.Unlocked(layer)) return Task.CompletedTask;
        var inputRender = currentRender;
        var dialog = new FormDialog(this, "Edit plot text") { Width = 520 };
        dialog.Note("Changes a unique matching Python string literal, or adds a per-panel override when the text is generated.");
        var pick = dialog.Choice("Plot label", texts, texts[0]);
        var text = dialog.Text("Replacement", texts[0].Text);
        pick.SelectionChanged += (_, _) => { if (pick.SelectedItem is PanelText t) text.Text = t.Text; };
        return dialog.Confirm("Apply", () => pick.SelectedItem is PanelText ? null : "Select a plot label.") &&
            pick.SelectedItem is PanelText selected && selected.Text != text.Text && StillEditing(inputRender)
            ? ApplyPlotText(figure, layer.Oid, selected.Key, selected.Text, text.Text) : Task.CompletedTask;
    }

    private Task EditClickedPlotText(FigureElement layer, PanelText hit)
    {
        if (hit.Box is [var x, var y, _, _] && FigureCanvas.Unlocked(layer)) OpenCanvasEditor(new Point(x, y), layer, hit);
        return Task.CompletedTask;
    }

    private Task ApplyPlotText(string figure, string oid, string key, string old, string text) => EditFigure(figure,
        new { figure, op = "panel_text", oid, key, old, text }, "Updating plot text…");

    private void StartPictureCrop()
    {
        if (Page.Selection is not { Kind: "image" } image || !FigureCanvas.Movable(image)) return;
        if (image.Props?.Rotation is not (null or 0) || image.Props?.Extra?.GetValueOrDefault("flip").ValueKind == JsonValueKind.String)
        { Status.Text = "Remove rotation and flip before cropping this picture."; return; }
        Page.SetTool(CanvasTool.Select);
        Page.BeginCrop(image.Oid);
        Page.Focus();
        Status.Text = "Drag the picture’s crop handles. Press Esc to leave crop mode.";
    }

    private Task CropImage()
    {
        if (shownFigure is not { } figure || Page.Selection is not { Kind: "image" } layer ||
            !FigureCanvas.Unlocked(layer) || !FigureCanvas.HasBox(layer)) return Task.CompletedTask;
        if (layer.Props?.Rotation is { } rotation && rotation != 0)
        { Status.Text = "Set the rotation to zero before cropping."; return Task.CompletedTask; }
        var inputRender = currentRender;
        Rect b = FigureCanvas.Box(layer);
        var crop = layer.Props?.Crop is [_, _, _, _] old ? old : [0, 0, 0, 0];
        double width = b.Width / (1 - crop[0] - crop[2]), height = b.Height / (1 - crop[1] - crop[3]);
        double[] values = new double[4];
        var dialog = new FormDialog(this, "Crop image");
        dialog.Note("Percent of the original image removed from each edge. Set all values to zero to restore the full image.");
        var fields = new[] { "Left (%)", "Top (%)", "Right (%)", "Bottom (%)" }
            .Select((label, i) => dialog.Text(label, FormatMm(crop[i] * 100))).ToArray();
        if (!dialog.Confirm("Apply", () =>
        {
            for (int i = 0; i < 4; i++)
                if (!TryMm(fields[i].Text, false, out values[i]) || values[i] < 0 || values[i] > 95)
                    return "Each edge must be between 0% and 95%.";
            return values[0] + values[2] > 95 || values[1] + values[3] > 95 ? "Keep at least 5% of each dimension." : null;
        })) return Task.CompletedTask;
        var box = new[] { b.X + (values[0] / 100 - crop[0]) * width, b.Y + (values[1] / 100 - crop[1]) * height,
            (1 - (values[0] + values[2]) / 100) * width, (1 - (values[1] + values[3]) / 100) * height };
        return StillEditing(inputRender) ? ApplyCrop(figure, layer.Oid, box) : Task.CompletedTask;
    }

    private Task ApplyCrop(string figure, string oid, double[] box) => EditFigure(figure,
        new { figure, op = "crop", oid, box }, "Cropping image…");

    private Task RotateImage()
    {
        if (shownFigure is not { } figure || Page.Selection is not { Kind: "image" } layer || !FigureCanvas.Unlocked(layer))
            return Task.CompletedTask;
        var inputRender = currentRender;
        double current = layer.Props?.Rotation ?? 0, angle = 0;
        var dialog = new FormDialog(this, "Rotate image");
        var input = dialog.Text("Angle (degrees counterclockwise)", FormatMm(current));
        return dialog.Confirm("Apply", () => TryMm(input.Text, false, out angle) ? null : "Enter an angle in degrees.") && StillEditing(inputRender)
            ? ApplyRotation(figure, layer.Oid, angle % 360 - current) : Task.CompletedTask;
    }

    private Task ApplyRotation(string figure, string oid, double by) => EditFigure(figure,
        new { figure, op = "rotate", oids = new[] { oid }, by }, "Rotating image…");

    private bool CanRotateSelected => !busy && validFigure && Page.Selections is { Length: > 0 } layers &&
        layers.All(e => FigureCanvas.Movable(e) && e.Kind is "panel" or "image" or "text");

    private Task RotateSelected(double by) => CanRotateSelected && shownFigure is { } figure
        ? EditFigure(figure, new { figure, op = "rotate", oids = Page.SelectedIds, by }, "Rotating layers…") : Task.CompletedTask;

    private Task FlipPicture(string axis) => IsEditableImage && shownFigure is { } figure
        ? EditFigure(figure, new { figure, op = "flip", oids = Page.SelectedIds, axis }, "Flipping picture…") : Task.CompletedTask;

    private bool StillEditing(FigureRender? original)
    {
        if (ReferenceEquals(original, currentRender)) return true;
        Status.Text = "The figure changed while the dialog was open. Reopen the dialog before applying edits.";
        return false;
    }
}
