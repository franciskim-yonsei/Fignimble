using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private sealed record PackageNeed(string Requirement, string Why);
    private sealed record PackageReport(string Python, PackageNeed[] Engine, PackageNeed[] Project, string? File);
    private sealed record PackageInstall(bool Ok, string Output);
    private static readonly JsonSerializerOptions PythonJson = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, string> pythonSelections = new(StringComparer.OrdinalIgnoreCase);
    private string PythonSettings => Path.Combine(settingsDirectory, "python.json");

    private void LoadPythonSettings()
    {
        try
        {
            if (File.Exists(PythonSettings) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PythonSettings)) is { } values)
                foreach (var (folder, exe) in values)
                    if (!string.IsNullOrWhiteSpace(exe)) pythonSelections[folder] = exe;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private string ProjectPython(string? folder)
    {
        if (Environment.GetEnvironmentVariable("FIGNIMBLE_PYTHON") is { } env && !string.IsNullOrWhiteSpace(env)) return env;
        if (folder is not null && pythonSelections.TryGetValue(folder, out var projectExe)) return projectExe;
        string discovered = EngineClient.ResolvePython(folder);
        if (folder is not null && new[] { ".venv", "venv" }.Any(v =>
            string.Equals(Path.Combine(folder, v, "Scripts", "python.exe"), discovered, StringComparison.OrdinalIgnoreCase))) return discovered;
        return pythonSelections.GetValueOrDefault("") ?? discovered;
    }

    private async Task ChoosePython()
    {
        if (busy || engine?.Remote is not null) return;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIGNIMBLE_PYTHON")))
        { Status.Text = "FIGNIMBLE_PYTHON is set. It takes precedence over the saved interpreter."; return; }
        if (CodeDirty || CaptionDirty) { Status.Text = "Apply or discard the current draft before changing Python."; return; }
        string? folder = project;
        var dialog = new FormDialog(this, "Python Setup");
        dialog.Note(folder is null ? "Choose the default Python. A project’s own .venv or venv still takes priority."
            : "Choose Python for this project. Applying reopens it and clears the app’s undo history. Existing terminal sessions keep their current environments.");
        var input = dialog.Text("Python executable (blank to autodetect)", ProjectPython(folder));
        var browse = new System.Windows.Controls.Button { Content = "Browse for Python…" };
        dialog.Add("", browse);
        browse.Click += (_, _) =>
        {
            var pick = new OpenFileDialog { Title = "Choose Python", Filter = "Python executable|*.exe", CheckFileExists = true };
            if (pick.ShowDialog(dialog) == true) input.Text = pick.FileName;
        };
        if (!dialog.ConfirmAsync("Use Python", async () =>
        {
            string requested = input.Text.Trim();
            if (requested != "" && !requested.Equals("python.exe", StringComparison.OrdinalIgnoreCase) &&
                (!Path.IsPathFullyQualified(requested) || !File.Exists(requested) || !Path.GetExtension(requested).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                return "Choose an existing Python executable by its full path.";
            string candidate = requested.Length == 0 ? EngineClient.ResolvePython(folder) : requested;
            await RunPythonTool<PackageReport>(candidate, folder, null);
            return null;
        })) return;
        string key = folder ?? "";
        string previous = pythonSelections.GetValueOrDefault(key) ?? "";
        string value = input.Text.Trim();
        if (value == "") pythonSelections.Remove(key); else pythonSelections[key] = value;
        try { SaveSetting(PythonSettings, pythonSelections); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (previous == "") pythonSelections.Remove(key); else pythonSelections[key] = previous;
            Status.Text = "Could not save Python setting: " + ex.Message;
            return;
        }
        if (folder is not null)
            await During("Opening project with selected Python…", async () =>
            {
                var selected = shownFigure;
                await GetEngine(folder).CallAsync<JsonElement>("open", new { project = folder });
                await RefreshProject(selected, external: true);
            });
        await CheckPythonPackages();
    }

    private async Task CheckPythonPackages()
    {
        if (busy) return;
        var client = engine;
        bool remote = client?.Remote is not null;
        string? folder = project;
        string exe = client?.Python ?? ProjectPython(folder);
        PackageReport? report = null;
        await During("Checking Python packages…", async () =>
        {
            report = remote ? await client!.CallAsync<PackageReport>("requirements") : await RunPythonTool<PackageReport>(exe, folder, null);
            Status.Text = report.Engine.Length + report.Project.Length == 0 ? $"Python packages are ready: {report.Python}" : $"Missing Python packages: {report.Python}";
        });
        if (report is null) return;
        var dialog = new FormDialog(this, "Check Python Packages") { Width = 475 };
        dialog.Note((remote ? "On the SSH server: " : "Local Python: ") + report.Python);
        var needs = report.Engine.Concat(report.Project).ToArray();
        if (needs.Length == 0) { dialog.Note("All declared requirements are satisfied."); dialog.Confirm("OK", () => null); return; }
        foreach (var item in needs) dialog.Note($"{item.Requirement} — {item.Why}");
        dialog.Note("Install only requirements from a project you trust. pip may run package build code in the selected environment.");
        if (!dialog.Confirm("Install listed packages", () => null)) return;
        var requirements = needs.Select(p => p.Requirement).Distinct().ToArray();
        await During("Installing listed Python packages…", async () =>
        {
            PackageInstall result = remote ? await client!.CallAsync<PackageInstall>("install", new { requirements })
                : await RunPythonTool<PackageInstall>(report.Python, folder, requirements);
            if (!result.Ok) throw new InvalidOperationException(result.Output);
            if (folder is not null && !remote)
            {
                engine?.Dispose();
                engine = null;
                await GetEngine(folder).CallAsync<JsonElement>("open", new { project = folder });
            }
            if (folder is not null) await RefreshProject(shownFigure, external: true);
            Status.Text = "Python packages installed. The figure renderer was refreshed.";
        });
    }

    private async void PythonSetupClicked(object sender, RoutedEventArgs e) => await ChoosePython();

    private void InstallCommandLineTool()
    {
        string directory = Path.Combine(settingsDirectory, "bin");
        var dialog = new FormDialog(this, "Install Command Line Tool");
        dialog.Note($"Install fignimble.cmd in {directory}. It is added to the PATH inside new Fignimble PowerShell sessions. Outside the app, run it by full path or add that directory to your user PATH yourself.");
        if (!dialog.Confirm("Install for this user", () => null)) return;
        try
        {
            string path = WriteCommandLineTool();
            Status.Text = "Installed " + path + ". Start a new terminal tab to use fignimble.";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private string WriteCommandLineTool()
    {
        string directory = Path.Combine(settingsDirectory, "bin");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "fignimble.cmd");
        static string Escape(string text)
        {
            if (text.Any(c => c is '\r' or '\n' or '"')) throw new ArgumentException("The command path is not valid for cmd.exe.");
            return text.Replace("%", "%%");
        }
        string python = Escape(ProjectPython(null));
        string root = Escape(EngineClient.FindEngine());
        string text = "@echo off\r\nsetlocal DisableDelayedExpansion\r\n" +
            $"if not defined FIGNIMBLE_SHELL_PYTHON set \"FIGNIMBLE_SHELL_PYTHON={python}\"\r\n" +
            $"set \"PYTHONPATH={root};%PYTHONPATH%\"\r\n" +
            "\"%FIGNIMBLE_SHELL_PYTHON%\" -X utf8 -m fignimble.cli %*\r\nexit /b %ERRORLEVEL%\r\n";
        // Use cmd.exe's system ANSI encoding for batch files on both UTF-8 and legacy Windows locales.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var encoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage,
            System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback);
        File.WriteAllText(path, text, encoding);
        return path;
    }

    private static async Task<T> RunPythonTool<T>(string python, string? folder, string[]? requirements)
    {
        bool installing = requirements is not null;
        var info = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (string item in new[] { "-I", "-X", "utf8", "-c",
            "import json,runpy,sys; " +
            "assert sys.version_info >= (3,10), 'Choose Python 3.10 or newer'; " +
            "m=runpy.run_path(sys.argv[1]); print(json.dumps(m['install'](json.loads(sys.argv[3])) if sys.argv[3] else m['missing'](sys.argv[2] or None)))",
            Path.Combine(EngineClient.FindEngine(), "fignimble", "requirements.py"), folder ?? "",
            installing ? JsonSerializer.Serialize(requirements) : "" }) info.ArgumentList.Add(item);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not launch Python.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(), errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(installing ? 1850 : 20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException(installing ? "Package installation timed out." : "Python did not answer the probe.");
        }
        string stdout = await output;
        string stderr = await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Trim().Split('\n').LastOrDefault() ?? "Python probe failed.");
        return JsonSerializer.Deserialize<T>(stdout, PythonJson) ?? throw new InvalidOperationException("Python returned no package information.");
    }
}
