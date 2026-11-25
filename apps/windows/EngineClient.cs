using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Fignimble.Windows;

internal sealed class EngineClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Process process;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> requests = new();
    private readonly Queue<string> errors = new();
    private readonly Task reader;
    private int nextId;

    public event Action<string>? Notification;
    public bool Running => !process.HasExited;
    public string Python { get; }
    public string EngineRoot { get; }
    public RemoteProject? Remote { get; }

    public EngineClient(string python, RemoteProject? remote = null)
    {
        string root = EngineRoot = FindEngine();
        Python = python;
        Remote = remote;
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

    private static string FindEngine()
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
                    Notification?.Invoke(notification);
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

    // The window serializes requests, including refreshes triggered by file changes.
    public async Task<T> CallAsync<T>(string command, object? arguments = null)
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
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, Json));
            await process.StandardInput.FlushAsync();
            int timeout = Remote is not null || command is "export" or "export_table" ? 620 : 200;
            JsonElement result = await request.Task.WaitAsync(TimeSpan.FromSeconds(timeout));
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("ok", out var ok) &&
                ok.ValueKind == JsonValueKind.False)
                throw new InvalidOperationException(result.TryGetProperty("error", out var error)
                    ? error.GetString() : "The engine refused the operation.");
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

internal sealed record RemoteProject(string Host, string Path, string Python, string Mirror);
internal sealed record ProjectState(FigureEntry[] Figures, bool CanUndo, bool CanRedo, string[]? History, string[]? Future,
    string[]? Panels);
internal sealed record FigureEntry(string Name);
internal sealed record FigureRender(string Figure, string File, double[]? Size, string? Background,
    FigureElement[]? Elements, FigureElement[]? Labels, FigureWarning[]? Warnings, string? Error, RenderJournal? Journal,
    FigureGuide[]? Guides, FigureCaption? Caption, FigureStyle? Style, FigureStyle? LabelStyle, RenderPoster? Poster);
internal sealed record RenderPoster(string Key, bool Landscape, double? Margin);
internal sealed record FigureStyle(Dictionary<string, JsonElement> Props);
internal sealed record FigureGuide(double? X, double? Y);
internal sealed record FigureCaption(string? Text);
internal sealed record FigureWarning(string Level, string Message);
internal sealed record FigureElement(string Oid, string? Kind, double[]? Box, Sprite? Sprite, string? Parent,
    string? Host, ElementProperties? Props, string? Method, string? Panel, string? Text, string? Group,
    SourceLocation? Code, PanelText[]? Texts, LayoutDivider[]? Dividers);
internal sealed record LayoutDivider(string Axis, double At, double From, double To, bool Editable);
internal sealed record SourceLocation(string? File);
internal sealed record PanelText(string Key, string Text, double[]? Box)
{
    public override string ToString() => $"{Key}: {Text}";
}
internal sealed record ElementProperties(bool Locked, bool Hidden, string? Name, string? Source, string? Text,
    JsonElement Label, double? X1, double? Y1, double? X2, double? Y2, double? Rotation, double[]? Crop, JsonElement[][]? Cells,
    JsonElement Data, double? Size, double? Width, double? Height)
{
    public string? LabelText => Label.ValueKind == JsonValueKind.String ? Label.GetString() : null;
    public string? DataText => Data.ValueKind == JsonValueKind.String ? Data.GetString() : null;
}
internal sealed record RenderJournal(string Key);
internal sealed record JournalPreset(string Name, Dictionary<string, double> Widths);
internal sealed record JournalChoice(string Key, string Name, Dictionary<string, double>? Widths = null)
{
    public override string ToString() => Name;
}
internal sealed record Sprite(string File, double X, double Y, double W, double H);
internal sealed record EditResult(FigureRender Figure, string[]? Select, bool Unchanged);
internal sealed record NewProjectResult(string Project, string Figure);
internal sealed record NewFigureResult(string Name);
internal sealed record ExportResult(string[]? Warnings);
