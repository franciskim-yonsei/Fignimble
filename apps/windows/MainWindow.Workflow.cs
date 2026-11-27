using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private const string LayerClipboardFormat = "Fignimble.EditableLayers";
    private static (string Project, string[] Code, string Token)? copiedLayers;
    private Dictionary<string, JsonElement>? copiedStyle;
    private static readonly string[] StyleKeys = ["color", "stroke", "fill", "width", "dash", "opacity", "size", "weight", "style", "font", "tip", "head", "radius", "arrow"];
    private sealed record CopiedCode(string[] Code);
    private sealed record SourceFile(string Path, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record HistoryStep(int Index, string Label, bool Current)
    {
        public override string ToString() => $"{Index}. {Label}{(Current ? "  (current)" : "")}";
    }

    private void MoreClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        void Add(string label, Func<Task> run, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            item.Click += async (_, _) => await run();
            menu.Items.Add(item);
        }
        Add("New poster from template…", ChoosePoster, project is not null);
        Add("Poster page settings…", PosterSettings, validFigure && currentRender?.Poster is not null);
        Add("Place another figure…", ChoosePlacedFigure, validFigure && state?.Figures is { Length: > 1 });
        Add("Search BioCartoon icons…", BrowseIcons, validFigure);
        var dark = new MenuItem { Header = "Dark figure preview", IsCheckable = true, IsChecked = darkPreview, IsEnabled = validFigure };
        dark.Click += async (_, _) => await SetDarkPreview(dark.IsChecked);
        menu.Items.Add(dark);
        Add("Color-vision preview…", ChooseVision, validFigure);
        Add("Caption", () => { ShowCaption(); return Task.CompletedTask; }, validFigure);
        Add("Plot style…", () => EditStyle(false), validFigure);
        Add("Panel lettering style…", () => EditStyle(true), validFigure);
        Add("History", ShowHistory, state is not null);
        menu.Items.Add(new Separator());
        Add("Add vertical guide…", () => ChooseGuide("x"), validFigure);
        Add("Add horizontal guide…", () => ChooseGuide("y"), validFigure);
        Add("Clear guides", () => ChangeGuide("guide_clear", "x", 0), validFigure && currentRender?.Guides is { Length: > 0 });
        var snap = new MenuItem { Header = "Smart snapping", IsCheckable = true, IsChecked = Page.SnapEnabled };
        snap.Click += (_, _) => UpdateViewAid(() => Page.SnapEnabled = snap.IsChecked);
        menu.Items.Add(snap);
        menu.Items.Add(new Separator());
        var layer = Page.Selection;
        bool editable = validFigure && layer is not null && FigureCanvas.Unlocked(layer);
        Add("Edit table cells…", EditTableCells, editable && layer?.Kind == "table" && layer.Props?.Cells is { Length: > 0 });
        Add("Edit plot text…", EditPlotText, editable && layer?.Kind == "panel" && layer.Texts is { Length: > 0 });
        Add("Crop image…", CropImage, editable && layer?.Kind == "image" && FigureCanvas.HasBox(layer));
        Add("Rotate image…", RotateImage, editable && layer?.Kind == "image");
        Add("Add image zoom inset", AddImageInset, editable && layer?.Kind == "image");
        Add("Add calibrated scale bar…", ChooseScaleBar, editable && layer?.Kind == "image");
        menu.Items.Add(new Separator());
        Add("Export all figures as PDF…", ChooseBatchFolder, state?.Figures is { Length: > 0 });
        Add("Export selected table…", ChooseTableExport, validFigure && Page.Selection?.Kind == "table");
        Add("Copy figure (vector + bitmap)", CopyFigureImage, validFigure);
        Add("Paste image", PasteFigureImage, validFigure);
        Add("Copy editable layers", CopyLayers, validFigure && Page.SelectedIds.Count > 0);
        Add("Paste editable layers", PasteLayers, validFigure && CanPasteLayers());
        menu.IsOpen = true;
    }

    private Task EditSource()
    {
        if (currentRender is not { } render) return Task.CompletedTask;
        var el = Page.Selection;
        SourceLocation? location = el?.Kind is "panel" or "inset" or "figure" or "text" ? el.Code ?? el.Site : el?.Site;
        var path = location?.File;
        if (path is null || !File.Exists(path)) path = render.File;
        ShowDrawer(CodeTab);
        if (OpenCodeFile(path))
        {
            int line = Math.Clamp(location?.Line ?? 1, 1, SourceText.Document.LineCount);
            SourceText.TextArea.Caret.Line = line;
            SourceText.ScrollToLine(line);
            SourceText.Focus();
        }
        return Task.CompletedTask;
    }

    private async Task ApplySource(string file, string original, string draft)
    {
        await engine!.CallAsync<JsonElement>("write_source", new { file, expected = original, text = draft, check_syntax = true });
    }

    private Task ApplyCaption(string figure, string original, string text) => EditFigure(figure,
        new { figure, op = "caption", expected = original, text }, "Saving caption…");

    private Task JumpHistory(int n) => During("Moving through edit history…", async () =>
    {
        string? current = shownFigure;
        Page.Select(null);
        try { await engine!.CallAsync<JsonElement>("undo_to", new { n }); }
        finally { await RefreshProject(current); }
    });

    private Task ChooseGuide(string axis)
    {
        if (currentRender?.Size is not [var width, var height]) return Task.CompletedTask;
        double limit = axis == "x" ? width : height, position = 0;
        var dialog = new FormDialog(this, axis == "x" ? "Add vertical guide" : "Add horizontal guide");
        var input = dialog.Text("Position (mm)", FormatMm(limit / 2));
        return dialog.Confirm("Add guide", () => TryMm(input.Text, false, out position) && position >= 0 && position <= limit
            ? null : "Enter a position on the page.") ? ChangeGuide("guide_add", axis, position) : Task.CompletedTask;
    }

    private Task ChangeGuide(string op, string axis, double position)
    {
        if (!validFigure || shownFigure is not { } figure) return Task.CompletedTask;
        return EditFigure(figure, new Dictionary<string, object> { ["figure"] = figure, ["op"] = op, [axis] = position }, "Updating guides…");
    }

    private Task ChooseTableExport()
    {
        if (!validFigure || shownFigure is not { } figure || TableToExport is not { } table) return Task.CompletedTask;
        var dialog = new SaveFileDialog { Title = "Export table",
            Filter = "Word table (*.docx)|*.docx|Excel sheet (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|TSV (*.tsv)|*.tsv",
            FileName = figure + "-table", InitialDirectory = project };
        return dialog.ShowDialog(this) == true ? ExportTable(figure, table.Oid, dialog.FileName) : Task.CompletedTask;
    }

    private Task ExportTable(string figure, string oid, string path) => During("Exporting editable table…", async () =>
    {
        string format = Path.GetExtension(path).ToLowerInvariant();
        if (format is not (".docx" or ".xlsx" or ".csv" or ".tsv"))
            throw new ArgumentException("Choose a .docx, .xlsx, .csv, or .tsv filename.");
        await engine!.CallAsync<JsonElement>("export", new { figure, oid, path, format = format[1..] });
        Status.Text = $"Exported {path}";
    });

    private Task CopyFigureImage() => !validFigure || shownFigure is not { } figure ? Task.CompletedTask :
        During("Copying figure…", async () =>
        {
            var folder = Directory.CreateTempSubdirectory("fignimble-clipboard-");
            try
            {
                string path = Path.Combine(folder.FullName, "figure.png");
                await engine!.CallAsync<ExportResult>("export", new { figure, path, format = "png", dpi = 300 });
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                string svg = Path.Combine(folder.FullName, "figure.svg"), pdf = Path.Combine(folder.FullName, "figure.pdf");
                await engine!.CallAsync<ExportResult>("export", new { figure, path = svg, format = "svg", dpi = 300 });
                await engine!.CallAsync<ExportResult>("export", new { figure, path = pdf, format = "pdf", dpi = 300 });
                var data = new DataObject();
                data.SetImage(decoder.Frames[0]);
                using var svgStream = new MemoryStream(File.ReadAllBytes(svg));
                using var pdfStream = new MemoryStream(File.ReadAllBytes(pdf));
                data.SetData("image/svg+xml", svgStream, false);
                data.SetData("Portable Document Format", pdfStream, false);
                Clipboard.SetDataObject(data, copy: true);
                Status.Text = "Copied SVG, PDF, and a 300 dpi bitmap. The receiving app chooses the format.";
            }
            finally { folder.Delete(recursive: true); }
        });

    private bool CanPasteLayers()
    {
        if (copiedLayers is not { } copy || !string.Equals(project, copy.Project, StringComparison.OrdinalIgnoreCase)) return false;
        try { return Clipboard.GetData(LayerClipboardFormat) is string token && token == copy.Token; }
        catch (System.Runtime.InteropServices.ExternalException) { return false; }
    }

    private Task CopyLayers() => CopyOrCutLayers(false);
    private Task CutLayers() => CopyOrCutLayers(true);

    private Task CopyOrCutLayers(bool cut) => !validFigure || (cut && !CanEditLayers("delete")) ||
        project is not { } folder || shownFigure is not { } figure ||
        Page.SelectedIds.Count == 0 ? Task.CompletedTask : During(cut ? "Cutting selected layers…" : "Copying selected layers…", async () =>
        {
            var oids = Page.SelectedIds.ToArray();
            var result = await CallForCurrentFigure<CopiedCode>("copy", new { figure, oids });
            if (result.Code.Length == 0 || cut && result.Code.Length != oids.Length)
            { Status.Text = "Some selected layers could not be copied as editable code."; return; }
            // The system clipboard only holds a marker. It cannot inject code into a project.
            string token = Guid.NewGuid().ToString("N");
            var clipboard = new DataObject();
            clipboard.SetData(LayerClipboardFormat, token);
            Clipboard.SetDataObject(clipboard);
            copiedLayers = (folder, result.Code, token);
            if (cut)
            {
                var deleted = await CallForCurrentFigure<EditResult>("edit", new { figure, op = "delete", oids });
                Page.Select(null);
                ShowRender(deleted.Figure);
                await ReadState(figure);
            }
            Status.Text = $"{(cut ? "Cut" : "Copied")} editable layers. Use Ctrl+V in a figure of this project.";
        });

    private Task CopyFromCanvas() => Page.SelectedIds.Count > 0 ? CopyLayers() : CopyFigureImage();

    private Task PasteIntoCanvas() => CanPasteLayers() ? PasteLayers() : PasteFigureImage();

    private void CopyLayerStyle()
    {
        var el = Page.Selections.FirstOrDefault(e => e.Oid == Page.SelectedIds.FirstOrDefault());
        if (el?.Props is not { } props) return;
        copiedStyle = (props.Extra ?? []).Where(field => StyleKeys.Contains(field.Key) && field.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(field => field.Key, field => field.Value);
        if (props.Size is { } size) copiedStyle["size"] = JsonSerializer.SerializeToElement(size);
        if (props.Width is { } width && el.Kind != "text") copiedStyle["width"] = JsonSerializer.SerializeToElement(width);
        Status.Text = "Copied layer style.";
    }

    private bool CanPasteStyle => !busy && validFigure && copiedStyle is { Count: > 0 } && Page.SelectedIds.Count > 0 && Page.Selections.All(FigureCanvas.Movable);
    private Task PasteLayerStyle() => CanPasteStyle && shownFigure is { } figure
        ? EditFigure(figure, new { figure, op = "paste_style", oids = Page.SelectedIds, props = copiedStyle }, "Pasting layer style…")
        : Task.CompletedTask;

    private Task PasteLayers() => !validFigure || shownFigure is not { } figure || !CanPasteLayers() ? Task.CompletedTask :
        During("Pasting editable layers…", async () =>
        {
            var result = await CallForCurrentFigure<EditResult>("paste", new { figure, code = copiedLayers!.Value.Code, dx = 3, dy = 3 });
            ShowRender(result.Figure);
            Page.SelectIds(result.Select ?? []);
            await ReadState(figure);
        });

    private Task PasteFigureImage() => !validFigure || currentRender is not { } render ? Task.CompletedTask :
        During("Pasting image…", async () =>
        {
            if (Clipboard.GetImage() is not { } bitmap) { Status.Text = "The clipboard does not contain an image."; return; }
            Rect box = FindPlacement(render);
            string path = Path.Combine(Path.GetTempPath(), $"fignimble-{Guid.NewGuid():N}.png");
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                var result = await CallForCurrentFigure<EditResult>("paste_image", new { figure = render.Figure, path, x = box.X, y = box.Y, w = box.Width });
                ShowRender(result.Figure);
                Page.SelectIds(result.Select ?? []);
                await ReadState(render.Figure);
            }
            finally { File.Delete(path); }
        });

    private Task ChooseBatchFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for the figure PDFs", InitialDirectory = project };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        return BatchExport(dialog.FolderName);
    }

    private Task BatchExport(string folder) => During("Exporting PDFs…", async () =>
    {
        await ReadState(shownFigure);
        var figures = state!.Figures;
        int exported = 0;
        var messages = new List<string>();
        foreach (var figure in figures)
        {
            string path = Path.Combine(folder, figure.Name + ".pdf");
            if (File.Exists(path)) { messages.Add($"{figure.Name}: skipped; PDF already exists."); continue; }
            try
            {
                Status.Text = $"Exporting {figure.Name}…";
                var result = await engine!.CallAsync<ExportResult>("export", new { figure = figure.Name, path, format = "pdf" });
                exported++;
                messages.AddRange((result.Warnings ?? []).Select(w => $"{figure.Name}: {w}"));
            }
            catch (Exception ex) { messages.Add($"{figure.Name}: {ex.Message}"); }
        }
        if (messages.Count > 0)
        {
            ShowExportMessages(messages);
            ShowDrawer(PreflightTab);
        }
        Status.Text = $"Exported {exported} of {figures.Length} figures to {folder}." +
            (messages.Count > 0 ? " See Preflight for batch details." : "");
    });
}
