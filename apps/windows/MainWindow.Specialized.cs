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
    private sealed record IconInfo(string Id, string Title, string? License, string? Credit, string? SvgUrl,
        string Source = "catalog", string? SvgKey = null, string? Author = null, double? Width = null, double? Height = null)
    {
        public override string ToString() => $"{Title} — {License}";
    }
    private sealed record IconSearch(IconInfo[] Icons);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed record PosterPreset(string Key, string Name, double[] Size)
    {
        public override string ToString() => $"{Name} · {Size[0]:g} × {Size[1]:g} mm";
    }

    private async Task PosterSettings()
    {
        if (currentRender is not { Poster: { } poster } render) return;
        PosterPreset[] presets = [];
        await During("Loading poster sizes…", async () =>
        {
            var sizes = await engine!.CallAsync<Dictionary<string, PosterPreset>>("posters");
            presets = sizes.Select(p => p.Value with { Key = p.Key }).ToArray();
        });
        if (!StillEditing(render)) return;
        var dialog = new FormDialog(this, "Poster page settings");
        dialog.Note("Orientation swaps the preset dimensions. It does not rearrange the existing layout; check the canvas and preflight after changing it.");
        var size = dialog.Choice("Size preset", presets, presets.FirstOrDefault(p => p.Key == poster.Key));
        size.IsEnabled = size.HasItems;
        var landscape = dialog.Check("Landscape", poster.Landscape);
        var margin = dialog.Text("Minimum safe margin (mm)", FormatMm(poster.Margin));
        double gap = 0;
        if (!dialog.Confirm("Apply", () => TryMm(margin.Text, false, out gap) && gap >= 0 ? null : "Enter a non-negative margin.") ||
            !StillEditing(render)) return;
        var props = new Dictionary<string, object?> { ["margin"] = gap };
        bool turned = landscape.IsChecked == true;
        string key = (size.SelectedItem as PosterPreset)?.Key ?? poster.Key;
        if (turned != poster.Landscape || key != poster.Key)
        {
            if (key != poster.Key) props["poster"] = key;
            props["landscape"] = turned;
            props["width"] = null;
            props["height"] = null;
        }
        await EditFigure(render.Figure, new { figure = render.Figure, op = "figure", props }, "Updating poster…");
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
        if (!ConfirmDiscardDrafts()) return;
        var result = await engine!.CallAsync<NewFigureResult>("new_figure", new { name, poster = template });
        await RefreshProject(result.Name);
    });

    private Task SetDarkPreview(bool value) => shownFigure is not { } figure ? Task.CompletedTask :
        During("Changing preview…", () => RenderFigure(figure, dark: value));

    private async Task BrowseIcons()
    {
        var search = new FormDialog(this, "Search BioCartoon");
        var term = search.Text("Search term", "");
        var category = search.Text("Category (optional)", "");
        var license = search.Text("License (optional)", "");
        var sort = search.Choice("Sort", new[] { "relevance", "popular", "az", "za" }, "relevance");
        search.Note("Search the icon catalog and review the license and credit before inserting. First use may download the catalog.");
        if (!search.Confirm("Search", () => new[] { term.Text, category.Text, license.Text }.All(string.IsNullOrWhiteSpace)
            ? "Enter a search term or filter." : null)) return;
        IconInfo[] icons = [];
        await During("Searching BioCartoon…", async () =>
        {
            icons = await SearchFilteredIcons(term.Text.Trim(), category.Text.Trim(), license.Text.Trim(), (string)sort.SelectedItem);
            Status.Text = icons.Length == 0 ? "No matching icons." : $"BioCartoon: {icons.Length} results.";
        });
        if (icons.Length == 0) return;
        var pick = new FormDialog(this, "Insert BioCartoon icon") { Width = 520 };
        var selected = pick.Choice("Icon", icons, icons[0]);
        var credit = pick.Text("Attribution (copy for your caption or acknowledgments)", CreditText(icons[0]), true);
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
            if (selected.SelectedItem is IconInfo icon) credit.Text = CreditText(icon);
        };
        if (pick.Confirm("Insert icon", () => null) && selected.SelectedItem is IconInfo chosen)
            await InsertIconRecord(chosen, !string.IsNullOrEmpty(chosen.SvgUrl ?? chosen.SvgKey));
    }

    private Task<IconInfo[]> SearchIcons(string term) => SearchFilteredIcons(term, "", "", "relevance");

    private async Task<IconInfo[]> SearchFilteredIcons(string term, string category, string license, string sort)
    {
        var args = new List<string> { "search", "--icons", "--no-sql", "--limit", "50", "--json", "--sort", sort };
        if (category.Length > 0) args.Add("--category=" + category);
        if (license.Length > 0) args.Add("--license=" + license);
        if (term.Length > 0) { args.Add("--"); args.Add(term); }
        string json = await BioCartoon(args.ToArray());
        return JsonSerializer.Deserialize<IconSearch>(json, WebJson)?.Icons ?? [];
    }

    private Task InsertIcon(string id, string title, bool svg) => InsertIconRecord(new IconInfo(id, title, null, null, null), svg);

    private Task InsertIconRecord(IconInfo icon, bool svg) => !validFigure || currentRender is not { Size: [var pageW, var pageH] } render ? Task.CompletedTask :
        During("Downloading BioCartoon icon…", async () =>
        {
            var folder = Directory.CreateTempSubdirectory("fignimble-icon-");
            try
            {
                string safeId = new(icon.Id.Where(char.IsAsciiLetterOrDigit).ToArray());
                if (safeId.Length == 0 || safeId != icon.Id) throw new IOException("Invalid icon identifier.");
                string destination(bool vector) => Path.Combine(folder.FullName, $"BioCartoon-{safeId}." + (vector ? "svg" : "png"));
                string path = destination(svg);
                try { await BioCartoon("fetch", svg ? "--svg" : "--png", "--out", path, "--", icon.Id); }
                catch (IOException) when (svg)
                {
                    path = destination(false);
                    svg = false;
                    await BioCartoon("fetch", "--png", "--out", path, "--", icon.Id);
                }
                IconAttribution.Write(path, JsonSerializer.Serialize(icon with { Source = icon.Source ?? "catalog" }, WebJson));
                double aspect = icon.Width is > 0 and var iw && icon.Height is > 0 and var ih ? ih / iw : 1;
                if (!svg)
                {
                    using var stream = File.OpenRead(path);
                    var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    aspect = (double)frame.PixelHeight / frame.PixelWidth;
                }
                double width = Math.Min(25, Math.Min(pageW, Math.Min(25, pageH) / aspect)), height = width * aspect;
                var result = await CallForCurrentFigure<EditResult>("import_file", new { figure = render.Figure, path,
                    x = (pageW - width) / 2, y = (pageH - height) / 2, w = width, label = (string?)null });
                ShowRender(result.Figure);
                Page.SelectIds(result.Select ?? []);
                await ReadState(render.Figure);
                Status.Text = $"Inserted {icon.Title} ({icon.Id}). Right-click the image to copy its attribution.";
            }
            finally { folder.Delete(recursive: true); }
        });

    private static string CreditText(IconInfo icon) => string.Join("\n", new[] { icon.Credit ?? icon.Author ?? icon.Title, icon.License }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    private IconInfo? SelectedIcon()
    {
        if (Page.Selection is not { Kind: "image", Props.Source: { } source } || project is not { } folder) return null;
        string path = Path.Combine(folder, source);
        try { return IconAttribution.Read(path) is { } json ? JsonSerializer.Deserialize<IconInfo>(json, WebJson) : null; }
        catch (JsonException) { return null; }
    }

    private void ShowIconCredit(IconInfo icon)
    {
        var dialog = new FormDialog(this, icon.Title);
        var credit = dialog.Text("Catalog attribution", CreditText(icon), multiline: true);
        credit.IsReadOnly = true;
        if (!dialog.Confirm("Copy Credit", () => null)) return;
        try { Clipboard.SetText(credit.Text); Status.Text = "Copied BioCartoon credit."; }
        catch (System.Runtime.InteropServices.ExternalException ex) { Status.Text = ex.Message; }
    }

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
        return SimulateVision(decoded.Frames[0], mode);
    }

    internal static BitmapSource SimulateVision(BitmapSource image, int mode)
    {
        var rgba = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        double[][] matrix = mode switch
        {
            0 => [[.367322,.860646,-.227968],[.280085,.672501,.047413],[-.011820,.042940,.968881]],
            1 => [[.152286,1.052583,-.204868],[.114503,.786281,.099216],[-.003882,-.048116,1.051998]],
            2 => [[1.255528,-.076749,-.178779],[-.078411,.930809,.147602],[.004733,.691367,.303900]],
            _ => [[.2126,.7152,.0722],[.2126,.7152,.0722],[.2126,.7152,.0722]]
        };
        double[] lut = Enumerable.Range(0, 256).Select(n => n <= 10 ? n / 3294.6 : Math.Pow((n / 255d + .055) / 1.055, 2.4)).ToArray();
        var encode = Enumerable.Range(0, 4097).Select(index => index / 4096d)
            .Select(t => (byte)Math.Round(255 * (t <= .0031308 ? t * 12.92 : 1.055 * Math.Pow(t, 1 / 2.4) - .055))).ToArray();
        byte Encode(double t) => encode[(int)Math.Round(Math.Clamp(t, 0, 1) * 4096)];
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
