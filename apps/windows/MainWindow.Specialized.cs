using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private bool darkPreview;
    private sealed record PosterTemplate(string Key, string Name, string Use, double[] Size)
    {
        public override string ToString() => $"{Name} · {Size[0]:g} × {Size[1]:g} mm";
    }
    private sealed record PosterTemplates(PosterTemplate[] Templates);
    private sealed record IconInfo(string Id, string Title, string License, string? Credit, string? SvgUrl)
    {
        public override string ToString() => $"{Title} — {License}";
    }
    private sealed record IconSearch(IconInfo[] Icons);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private Task PosterSettings()
    {
        if (currentRender is not { Poster: { } poster } render) return Task.CompletedTask;
        var dialog = new FormDialog(this, "Poster page settings");
        dialog.Note("Orientation swaps the preset dimensions. It does not rearrange the existing layout; check the canvas and preflight after changing it.");
        var landscape = dialog.Check("Landscape", poster.Landscape);
        var margin = dialog.Text("Minimum safe margin (mm)", FormatMm(poster.Margin));
        double gap = 0;
        if (!dialog.Confirm("Apply", () => TryMm(margin.Text, false, out gap) && gap >= 0 ? null : "Enter a non-negative margin.") ||
            !StillEditing(render)) return Task.CompletedTask;
        var props = new Dictionary<string, object?> { ["margin"] = gap };
        bool turned = landscape.IsChecked == true;
        if (turned != poster.Landscape)
        {
            props["landscape"] = turned;
            props["width"] = null;
            props["height"] = null;
        }
        return EditFigure(render.Figure, new { figure = render.Figure, op = "figure", props }, "Updating poster…");
    }

    private Task ChoosePlacedFigure()
    {
        if (currentRender is not { Size: [var w, var h] } render || project is not { } folder) return Task.CompletedTask;
        string[] figures = (state?.Figures ?? []).Select(f => f.Name).Where(n => n != render.Figure).ToArray();
        if (figures.Length == 0) return Task.CompletedTask;
        var dialog = new FormDialog(this, "Place another figure");
        dialog.Note("The placed figure is drawn from its source each time. Edit it separately to update the inset or poster.");
        var choice = dialog.Choice("Figure", figures, figures[0]);
        var x = dialog.Text("Left (mm)", FormatMm(Math.Min(20, w / 10)));
        var y = dialog.Text("Top (mm)", FormatMm(Math.Min(100, h / 6)));
        var width = dialog.Text("Width (mm)", FormatMm(w / 3));
        double left = 0, top = 0, span = 0;
        if (!dialog.Confirm("Place figure", () => TryMm(x.Text, false, out left) && TryMm(y.Text, false, out top) &&
             TryMm(width.Text, true, out span) ? null : "Enter coordinates and a positive width.") || !StillEditing(render)) return Task.CompletedTask;
        return AddPanel("import_file", "Placing figure…", (figure, _) => new { figure,
            path = Path.Combine(folder, (string)choice.SelectedItem + ".py"), x = left, y = top, w = span });
    }

    private async Task ChoosePoster()
    {
        PosterTemplate[] choices = [];
        await During("Loading poster templates…", async () => choices = (await engine!.CallAsync<PosterTemplates>("poster_templates")).Templates);
        if (choices.Length == 0) return;
        var dialog = new FormDialog(this, "New poster from template");
        dialog.Note("Creates an editable title, authors, section text, and a placeholder QR code. When the project already has a figure, the template includes it.");
        var template = dialog.Choice("Size and orientation", choices, choices[0]);
        var name = dialog.Text("File name", "poster");
        if (dialog.Confirm("Create poster", () => string.IsNullOrWhiteSpace(name.Text) ? "Enter a figure name." : null))
            await CreatePoster(name.Text.Trim(), ((PosterTemplate)template.SelectedItem).Key);
    }

    private Task CreatePoster(string name, string template) => During("Creating poster…", async () =>
    {
        var result = await engine!.CallAsync<NewFigureResult>("new_figure", new { name, poster = template });
        await RefreshProject(result.Name);
    });

    private Task SetDarkPreview(bool value) => shownFigure is not { } figure ? Task.CompletedTask : During("Changing preview…", async () =>
    {
        await RenderDark(figure, value);
    });

    private async Task RenderDark(string figure, bool value)
    {
        var result = await engine!.CallAsync<FigureRender>("figure", new { name = figure, dark = value });
        darkPreview = value;
        ShowRender(result);
    }

    private async Task BrowseIcons()
    {
        var search = new FormDialog(this, "Search BioCartoon");
        var term = search.Text("Search term", "");
        search.Note("Search the icon catalog and review the license and credit before inserting. First use may download the catalog.");
        if (!search.Confirm("Search", () => string.IsNullOrWhiteSpace(term.Text) ? "Enter a search term." : null)) return;
        IconInfo[] icons = [];
        await During("Searching BioCartoon…", async () =>
        {
            icons = await SearchIcons(term.Text.Trim());
            Status.Text = icons.Length == 0 ? "No matching icons." : $"BioCartoon: {icons.Length} results.";
        });
        if (icons.Length == 0) return;
        var pick = new FormDialog(this, "Insert BioCartoon icon") { Width = 520 };
        var selected = pick.Choice("Icon", icons, icons[0]);
        var credit = pick.Text("Attribution (copy for your caption or acknowledgments)", icons[0].Credit ?? icons[0].License, true);
        credit.IsReadOnly = true;
        var preview = new Button { Content = "Preview selected icon" };
        preview.Click += async (_, _) =>
        {
            if (selected.SelectedItem is not IconInfo icon) return;
            preview.IsEnabled = false;
            try
            {
                var temp = Directory.CreateTempSubdirectory("fignimble-icon-preview-");
                try
                {
                    string path = Path.Combine(temp.FullName, "icon.png");
                    await BioCartoon("fetch", "--png", "--max-side", "420", "--out", path, "--", icon.Id);
                    using var stream = File.OpenRead(path);
                    var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    if (pick.IsVisible) new Window { Owner = pick, Title = icon.Title, Width = 460, Height = 420,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Image { Source = image, Margin = new Thickness(15) } }.ShowDialog();
                }
                finally { temp.Delete(recursive: true); }
            }
            catch (Exception ex) { if (pick.IsVisible) MessageBox.Show(pick, ex.Message, "BioCartoon preview"); }
            finally { preview.IsEnabled = true; }
        };
        pick.Add("Preview", preview);
        selected.SelectionChanged += (_, _) =>
        {
            if (selected.SelectedItem is IconInfo icon) credit.Text = icon.Credit ?? icon.License;
        };
        if (pick.Confirm("Insert icon", () => null) && selected.SelectedItem is IconInfo chosen)
            await InsertIcon(chosen.Id, chosen.Title, !string.IsNullOrEmpty(chosen.SvgUrl));
    }

    private async Task<IconInfo[]> SearchIcons(string term)
    {
        string json = await BioCartoon("search", "--icons", "--no-sql", "--limit", "30", "--json", "--", term);
        return JsonSerializer.Deserialize<IconSearch>(json, WebJson)?.Icons ?? [];
    }

    private Task InsertIcon(string id, string title, bool svg) => !validFigure || currentRender is not { } render ? Task.CompletedTask :
        During("Downloading BioCartoon icon…", async () =>
        {
            var folder = Directory.CreateTempSubdirectory("fignimble-icon-");
            try
            {
                string safeId = new(id.Where(char.IsAsciiLetterOrDigit).ToArray());
                if (safeId.Length == 0 || safeId != id) throw new IOException("Invalid icon identifier.");
                string path = Path.Combine(folder.FullName, $"BioCartoon-{safeId}." + (svg ? "svg" : "png"));
                await BioCartoon("fetch", id, svg ? "--svg" : "--png", "--out", path);
                Rect box = FindPlacement(render);
                var result = await engine!.CallAsync<EditResult>("import_file", new { figure = render.Figure, path,
                    x = box.X, y = box.Y, w = box.Width, label = (string?)null });
                ShowRender(result.Figure);
                Page.SelectIds(result.Select ?? []);
                await ReadState(render.Figure);
                Status.Text = $"Inserted {title} ({id}). Keep the catalog’s attribution with the figure.";
            }
            finally { folder.Delete(recursive: true); }
        });

    private async Task<string> BioCartoon(params string[] args)
    {
        string? scripts = engine?.Python is { } python && Path.IsPathFullyQualified(python) ? Path.GetDirectoryName(python) : null;
        string? host = Environment.GetEnvironmentVariable("FIGNIMBLE_BIOCARTOON");
        if (string.IsNullOrWhiteSpace(host) && scripts is not null && File.Exists(Path.Combine(scripts, "biocartoon.exe")))
            host = Path.Combine(scripts, "biocartoon.exe");
        var start = new ProcessStartInfo(host ?? "biocartoon.exe") { RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, CreateNoWindow = true, UseShellExecute = false };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var proc = Process.Start(start) ?? throw new IOException("Could not start BioCartoon.");
        var output = proc.StandardOutput.ReadToEndAsync();
        var errors = proc.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!proc.HasExited) proc.Kill(true); throw new IOException("BioCartoon timed out."); }
        if (proc.ExitCode != 0) throw new IOException("BioCartoon: " + (await errors).Trim());
        return await output;
    }

    private async Task ChooseVision()
    {
        if (currentRender is not { } render || render.Size is not [var w, var h]) return;
        var dialog = new FormDialog(this, "Color-vision preview");
        string[] modes = ["Deuteranopia", "Protanopia", "Tritanopia", "Grayscale"];
        var mode = dialog.Choice("Simulation", modes, modes[0]);
        dialog.Note("Approximate preview only. It does not change the source or exported colors.");
        if (!dialog.Confirm("Preview", () => null)) return;
        int index = mode.SelectedIndex;
        BitmapSource? bitmap = null;
        await During("Rendering color-vision preview…", async () =>
        {
            var temp = Directory.CreateTempSubdirectory("fignimble-vision-");
            try
            {
                string path = Path.Combine(temp.FullName, "preview.png");
                int dpi = Math.Clamp((int)(1200 * 25.4 / Math.Max(w, h)), 10, 150);
                await engine!.CallAsync<ExportResult>("export", new { figure = render.Figure, path, format = "png", dpi, dark = darkPreview });
                bitmap = await Task.Run(() => VisionBitmap(path, index));
            }
            finally { temp.Delete(recursive: true); }
            Status.Text = "Color-vision simulation does not affect exports.";
        });
        if (bitmap is null) return;
        new Window { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Title = modes[index] + " preview",
            Width = 750, Height = 600, Content = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(10) } }.ShowDialog();
    }

    private static BitmapSource VisionBitmap(string path, int mode)
    {
        using var stream = File.OpenRead(path);
        var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var rgba = new FormatConvertedBitmap(decoded.Frames[0], PixelFormats.Bgra32, null, 0);
        double[][] matrix = mode switch
        {
            0 => [[.367322,.860646,-.227968],[.280085,.672501,.047413],[-.011820,.042940,.968881]],
            1 => [[.152286,1.052583,-.204868],[.114503,.786281,.099216],[-.003882,-.048116,1.051998]],
            2 => [[1.255528,-.076749,-.178779],[-.078411,.930809,.147602],[.004733,.691367,.303900]],
            _ => [[.2126,.7152,.0722],[.2126,.7152,.0722],[.2126,.7152,.0722]]
        };
        double[] lut = Enumerable.Range(0, 256).Select(n => n <= 10 ? n / 3294.6 : Math.Pow((n / 255d + .055) / 1.055, 2.4)).ToArray();
        byte Encode(double t) { t = Math.Clamp(t, 0, 1); return (byte)Math.Round(255 * (t <= .0031308 ? t * 12.92 : 1.055 * Math.Pow(t, 1 / 2.4) - .055)); }
        int stride = rgba.PixelWidth * 4;
        byte[] pixels = new byte[stride * rgba.PixelHeight];
        rgba.CopyPixels(pixels, stride, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double r = lut[pixels[i + 2]], g = lut[pixels[i + 1]], b = lut[pixels[i]];
            for (int c = 0; c < 3; c++) pixels[i + 2 - c] = Encode(matrix[c][0] * r + matrix[c][1] * g + matrix[c][2] * b);
        }
        var result = BitmapSource.Create(rgba.PixelWidth, rgba.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
