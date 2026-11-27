using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fignimble.Windows;

internal sealed class EngineClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Process process;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> requests = new();
    private readonly Queue<string> errors = new();
    private readonly Task reader;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private int nextId;

    public event Action<string>? Notification;
    public bool Running => !process.HasExited;
    public string Python { get; }
    public string EngineRoot { get; }
    public RemoteProject? Remote { get; }
    internal InteractiveSshSession? Ssh { get; }
    public RemotePackageNeeds? MissingRemotePackages { get; private set; }

    public EngineClient(string python, RemoteProject? remote = null, InteractiveSshSession? ssh = null)
    {
        string root = EngineRoot = FindEngine();
        Python = python;
        Remote = remote;
        Ssh = ssh;
        var utf8 = new UTF8Encoding(false);
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = utf8, StandardOutputEncoding = utf8, StandardErrorEncoding = utf8,
        };
        string[] args = remote is null ? ["-u", "-m", "fignimble.rpc"] :
            ["-u", "-m", "fignimble.tunnel", "--host=" + remote.Host, "--path=" + remote.Path,
             "--mirror=" + remote.Mirror, "--python=" + remote.Python];
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment.TryGetValue("PYTHONPATH", out string? pythonPath);
        start.Environment["PYTHONPATH"] = string.IsNullOrEmpty(pythonPath) ? root : root + Path.PathSeparator + pythonPath;
        // The worker should use the same interpreter that was actually launched (sys.executable).
        start.Environment.Remove("FIGNIMBLE_PYTHON");
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        start.Environment.Remove("FIGNIMBLE_SSH_SESSION_HOST");
        start.Environment.Remove("FIGNIMBLE_SSH_PIPE");
        if (ssh is not null)
        {
            start.Environment["FIGNIMBLE_SSH_SESSION_HOST"] = ssh.Host;
            start.Environment["FIGNIMBLE_SSH_PIPE"] = ssh.PipeName;
        }
        process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch Python.");
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (errors)
            {
                if (errors.Count == 12) errors.Dequeue();
                errors.Enqueue(e.Data);
            }
        };
        process.BeginErrorReadLine();
        reader = ReadAsync();
    }

    public static string ResolvePython(string? project)
    {
        string? configured = Environment.GetEnvironmentVariable("FIGNIMBLE_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        string? local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates = [
            .. (project is null ? Array.Empty<string>() : new[] { Path.Combine(project, ".venv", "Scripts", "python.exe"),
                Path.Combine(project, "venv", "Scripts", "python.exe") }),
            Path.GetFullPath(Path.Combine(FindEngine(), "..", ".venv", "Scripts", "python.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "python", "Scripts", "python.exe")),
            Path.Combine(local, "Fignimble", "python", "Scripts", "python.exe")
        ];
        return candidates.FirstOrDefault(File.Exists) ?? "python.exe";
    }

    internal static string FindEngine()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("FIGNIMBLE_ENGINE");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (File.Exists(Path.Combine(explicitPath, "fignimble", "rpc.py"))) return Path.GetFullPath(explicitPath);
            throw new DirectoryNotFoundException($"FIGNIMBLE_ENGINE does not point to a Fignimble engine: {explicitPath}");
        }
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "engine");
            if (File.Exists(Path.Combine(candidate, "fignimble", "rpc.py"))) return candidate;
        }
        throw new DirectoryNotFoundException("Fignimble's Python engine was not found. Run the app from the source checkout.");
    }

    private async Task ReadAsync()
    {
        Exception? failure = null;
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                JsonElement reply = document.RootElement;
                if (reply.TryGetProperty("event", out var kind) && kind.GetString() is { } notification)
                {
                    if (notification == "needs_packages") MissingRemotePackages = reply.Deserialize<RemotePackageNeeds>(Json);
                    Notification?.Invoke(notification);
                }
                else if (reply.TryGetProperty("id", out var id) && id.TryGetInt32(out int i) &&
                         requests.TryRemove(i, out var request))
                {
                    if (reply.TryGetProperty("error", out var error))
                        request.TrySetException(new InvalidOperationException(error.GetString()));
                    else if (reply.TryGetProperty("result", out var result)) request.TrySetResult(result.Clone());
                    else request.TrySetException(new InvalidOperationException("The engine returned no result."));
                }
            }
            // Wait for redirected stderr to drain so startup errors are visible.
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            foreach (var request in requests.Values) request.TrySetException(failure ?? Stopped());
            if (Remote is not null) Notification?.Invoke("disconnected");
        }
    }

    private InvalidOperationException Stopped()
    {
        lock (errors)
        {
            return new InvalidOperationException("The Fignimble Python engine stopped. Check its Python dependencies." +
                (errors.Count == 0 ? "" : "\n" + string.Join("\n", errors)));
        }
    }

    // Serialize the outgoing lines; a background thumbnail refresh may overlap a foreground command.
    public async Task<T> CallAsync<T>(string command, object? arguments = null, double? pxPerMM = null)
    {
        int id = Interlocked.Increment(ref nextId);
        var request = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        requests[id] = request;
        try
        {
            if (reader.IsCompleted || !Running) throw Stopped();
            var message = new Dictionary<string, object?> { ["id"] = id, ["cmd"] = command };
            if (arguments is not null)
                foreach (var field in JsonSerializer.SerializeToElement(arguments, Json).EnumerateObject())
                    message.Add(field.Name, field.Value);
            if (pxPerMM is { } density) message.Add("pxPerMM", density);
            await sendGate.WaitAsync();
            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, Json));
                await process.StandardInput.FlushAsync();
            }
            finally { sendGate.Release(); }
            int timeout = command is "install" or "install_remote_engine_packages" ? 1850
                : Remote is not null || command is "export" or "export_table" or "export_source_data" ? 620 : 200;
            JsonElement result = await request.Task.WaitAsync(TimeSpan.FromSeconds(timeout));
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("ok", out var ok) &&
                ok.ValueKind == JsonValueKind.False)
                throw new InvalidOperationException(result.TryGetProperty("error", out var error)
                    ? error.GetString() : result.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String
                        ? output.GetString() : "The engine refused the operation.");
            return result.Deserialize<T>(Json) ?? throw new InvalidOperationException("Empty engine reply.");
        }
        finally { requests.TryRemove(id, out _); }
    }

    public void Dispose()
    {
        try
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(Remote is null ? 1000 : 5000)) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (IOException) { }
        finally { process.Dispose(); }
    }
}

internal sealed record RemoteProject(string Host, string Path, string Python, string Mirror, bool Interactive = false);
internal sealed record RemotePackageNeeds(string[] Missing, string[] Pip);
internal sealed record ProjectState(FigureEntry[] Figures, bool CanUndo, bool CanRedo, string[]? History, string[]? Future,
    string[]? Panels);
internal sealed record FigureEntry(string Name, string? Thumb, string? Error, int Warnings);
internal sealed record FigureRender(string Figure, string File, double[]? Size, string? Background,
    FigureElement[]? Elements, FigureElement[]? Labels, FigureWarning[]? Warnings, string? Error, RenderJournal? Journal,
    FigureGuide[]? Guides, FigureCaption? Caption, FigureStyle? Style, FigureStyle? LabelStyle, RenderPoster? Poster, string? Thumb,
    double? PxPerMM);
internal sealed record RenderPoster(string Key, bool Landscape, double? Margin);
internal sealed record FigureStyle(Dictionary<string, JsonElement> Props);
internal sealed record FigureGuide(double? X, double? Y);
internal sealed record FigureCaption(string? Text);
internal sealed record FigureWarning(string Level, string Message, string? Oid);
internal sealed record FigureElement(string Oid, string? Kind, double[]? Box, Sprite? Sprite, string? Parent,
    string? Host, ElementProperties? Props, string? Method, string? Panel, string? Text, string? Group,
    SourceLocation? Code, PanelText[]? Texts, LayoutDivider[]? Dividers, SourceLocation? Site, string? Error, TableGrid? Grid, bool Detached, string? Inner, Dictionary<string, JsonElement>? Item, PanelMark[]? Marks);
internal sealed record TableGrid(double[][] Rows, double[][] Cols);
internal sealed record LayoutDivider(string Axis, double At, double From, double To, bool Editable);
internal sealed record SourceLocation(string? File, int? Line);
internal sealed record PanelMark(string Key, string Color, double[]? Box);
internal sealed record PanelText(string Key, string Text, double[]? Box, double Size, string? Color, bool Bold, bool Italic)
{
    public override string ToString() => $"{Key}: {Text}";
}
internal sealed record ElementProperties(bool Locked, bool Hidden, string? Name, string? Source, string? Text,
    JsonElement Label, double? X1, double? Y1, double? X2, double? Y2, double? Rotation, double[]? Crop, JsonElement[][]? Cells,
    JsonElement Data, double? Size, double? Width, double? Height, double? H, string? Align)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
    public string? LabelText => Label.ValueKind == JsonValueKind.String ? Label.GetString() : null;
    public string? DataText => Data.ValueKind == JsonValueKind.String ? Data.GetString() : null;
}
internal sealed record RenderJournal(string Key);
internal sealed record JournalPreset(string Name, Dictionary<string, double> Widths, Dictionary<string, int>? Dpi, Dictionary<string, JsonElement>? Labels);
internal sealed record JournalChoice(string Key, string Name, Dictionary<string, double>? Widths = null, Dictionary<string, int>? Dpi = null, Dictionary<string, JsonElement>? Labels = null)
{
    public override string ToString() => Name;
}
internal sealed record Sprite(string File, double X, double Y, double W, double H);
internal sealed record EditResult(FigureRender Figure, string[]? Select, bool Unchanged, string? File);
internal sealed record NewProjectResult(string Project, string Figure);
internal sealed record NewFigureResult(string Name);
internal sealed record ExportResult(string[]? Warnings, ExportResolution? Resolution);
internal sealed record ExportResolution(int? Dpi, string[]? Notes);
