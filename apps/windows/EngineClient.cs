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

    public EngineClient()
    {
        string root = FindEngine();
        string python = Environment.GetEnvironmentVariable("FIGNIMBLE_PYTHON") ??
            (File.Exists(Path.Combine(root, "..", ".venv", "Scripts", "python.exe"))
                ? Path.GetFullPath(Path.Combine(root, "..", ".venv", "Scripts", "python.exe")) : "python.exe");
        var utf8 = new UTF8Encoding(false);
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = utf8, StandardOutputEncoding = utf8, StandardErrorEncoding = utf8,
        };
        foreach (string arg in new[] { "-u", "-m", "fignimble.rpc" }) start.ArgumentList.Add(arg);
        start.Environment.TryGetValue("PYTHONPATH", out string? pythonPath);
        start.Environment["PYTHONPATH"] = string.IsNullOrEmpty(pythonPath) ? root : root + Path.PathSeparator + pythonPath;
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
            JsonElement result = await request.Task.WaitAsync(TimeSpan.FromSeconds(200));
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
            if (!process.WaitForExit(1000)) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (IOException) { }
        finally { process.Dispose(); }
    }
}

internal sealed record ProjectState(FigureEntry[] Figures, bool CanUndo, bool CanRedo);
internal sealed record FigureEntry(string Name);
internal sealed record FigureRender(string Figure, string File, double[]? Size, string? Background,
    FigureElement[]? Elements, FigureElement[]? Labels, string? Error);
internal sealed record FigureElement(string Oid, string? Kind, double[] Box, Sprite? Sprite, string? Parent, string? Host);
internal sealed record Sprite(string File, double X, double Y, double W, double H);
internal sealed record EditResult(FigureRender Figure);
