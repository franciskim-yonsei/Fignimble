using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private Task ChooseNewTable()
    {
        if (project is null || busy) return Task.CompletedTask;
        var dialog = new FormDialog(this, "New table");
        dialog.Note("Creates a standalone figure and a CSV file. The first row is the header.");
        var name = dialog.Text("Table name", "table");
        var rows = dialog.Text("Rows (including header)", "4");
        var columns = dialog.Text("Columns", "3");
        int r = 0, c = 0;
        return dialog.Confirm("Create table", () => string.IsNullOrWhiteSpace(name.Text) ? "Enter a table name." :
            !int.TryParse(rows.Text, out r) || r is < 1 or > 100 || !int.TryParse(columns.Text, out c) || c is < 1 or > 30
                ? "Enter 1–100 rows and 1–30 columns." : null)
            ? CreateTable(name.Text.Trim(), r, c) : Task.CompletedTask;
    }

    private Task CreateTable(string name, int rows, int cols) => During("Creating table…", async () =>
    {
        if (!ConfirmDiscardDrafts()) return;
        var result = await engine!.CallAsync<NewFigureResult>("new_table", new { name, rows, cols });
        await RefreshProject(result.Name);
        if (StandaloneTable is { } table) Page.Select(table.Oid);
    });

    private FigureElement? StandaloneTable => currentRender?.Elements?.Where(FigureCanvas.TopLevel).ToArray() is [{ Kind: "table" } table]
        ? table : null;
    private FigureElement? TableToExport => Page.Selection is { Kind: "table" } table ? table : StandaloneTable;

    private void OpenInlineCell(FigureElement table, int row, int column)
    {
        if (busy || !validFigure || inlineText is not null || shownFigure is not { } figure ||
            table.Props?.Cells is not { } cells || !FigureCanvas.CellBox(table, row, column, out Rect box) ||
            row >= cells.Length) return;
        var originalCell = column < cells[row].Length ? cells[row][column] : default;
        string original = originalCell.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => "",
            JsonValueKind.String => originalCell.GetString() ?? "",
            _ => originalCell.GetRawText()
        };
        var editor = new TextBox { Text = original, Width = Math.Max(4, box.Width), Height = Math.Max(3, box.Height),
            FontSize = Math.Clamp(Math.Min(box.Height - 0.5, (table.Props?.Size ?? 8) * 25.4 / 72), 2, 12),
            BorderThickness = new Thickness(0.3), Padding = new Thickness(0.5, 0, 0.5, 0),
            VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(editor, $"Table row {row + 1}, column {column + 1}");
        Canvas.SetLeft(editor, box.X);
        Canvas.SetTop(editor, box.Y);
        inlineText = editor;
        CanvasEditors.Children.Add(editor);
        async Task Finish(bool save, int step = 0, bool focus = false)
        {
            if (inlineText != editor) return;
            inlineText = null;
            CanvasEditors.Children.Remove(editor);
            if (focus) Page.Focus();
            if (!save || figure != shownFigure) return;
            if (original != editor.Text)
            {
                object? expected = originalCell.ValueKind == JsonValueKind.Undefined ? null : originalCell;
                await EditFigure(figure, new { figure, op = "table_cell", oid = table.Oid, row, column, expected, text = editor.Text }, "Saving table cell…");
            }
            if (step == 0 || currentRender?.Elements?.FirstOrDefault(e => e.Oid == table.Oid && e.Kind == "table") is not { } latest ||
                latest.Grid?.Cols is not { Length: > 0 } widths || latest.Props?.Cells is not { Length: > 0 } rows) return;
            int cell = row * widths.Length + column + step;
            if (cell >= 0 && cell < rows.Length * widths.Length) OpenInlineCell(latest, cell / widths.Length, cell % widths.Length);
        }
        editor.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key is not (Key.Tab or Key.Enter or Key.Escape)) return;
            e.Handled = true;
            await Finish(e.Key != Key.Escape, e.Key == Key.Tab ? Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1 : 0, true);
        };
        editor.LostKeyboardFocus += async (_, _) => await Finish(true);
        editor.Focus();
        editor.SelectAll();
    }
}
