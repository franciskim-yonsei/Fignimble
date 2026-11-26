using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private string? codePath, codeFigurePath;
    private string codeOriginal = "", renderedSource = "";
    private bool syncingCode, restoringFigure, closeAfterApply;
    private static string NormalizeSource(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private bool CodeDirty => codePath is not null && NormalizeSource(SourceText.Text) != codeOriginal;

    private void SetupCode()
    {
        SourcePicker.SelectionChanged += (_, _) =>
        {
            if (syncingCode || SourcePicker.SelectedItem is not SourceFile file) return;
            if (!OpenCodeFile(file.Path)) SelectCodePicker();
        };
        SourceText.TextChanged += (_, _) => { if (!syncingCode) UpdateCodeControls(); };
        SourceText.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                await SaveCode();
            }
        };
    }

    private void UpdateCodeControls()
    {
        SourceText.IsReadOnly = busy || codePath is null;
        SourcePicker.IsEnabled = !busy && SourcePicker.HasItems;
        EditSourceButton.IsEnabled = !busy && CodeDirty;
        RevertCodeButton.IsEnabled = ExternalCodeButton.IsEnabled = !busy && codePath is not null;
        SourceTitle.Text = CodeDirty ? "Unsaved" : "";
    }

    private bool ConfirmDiscardCode() => !CodeDirty || MessageBox.Show(this,
        $"Discard the unsaved changes to {Path.GetFileName(codePath)}? Choose Cancel to keep editing or apply them first.",
        "Unsaved code", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private bool ConfirmDiscardDrafts() => ConfirmDiscardCode() && ConfirmDiscardCaption();

    private void SelectCodePicker()
    {
        syncingCode = true;
        try { SourcePicker.SelectedItem = SourcePicker.Items.Cast<SourceFile>().FirstOrDefault(f => SameCodePath(f.Path, codePath)); }
        finally { syncingCode = false; }
    }

    // Windows GetFullPath expands existing 8.3 aliases and normalizes path separators.
    private static string NormalizeCodePath(string path) => Path.GetFullPath(path);

    private static bool SameCodePath(string? a, string? b) => a is null || b is null ? a == b :
        string.Equals(NormalizeCodePath(a), NormalizeCodePath(b), StringComparison.OrdinalIgnoreCase);

    private void RefreshCodeFiles()
    {
        var paths = project is { } folder && currentRender is { } render
            ? new[] { render.File, Path.Combine(folder, "style.py"), codePath }
                .Concat((state?.Panels ?? []).Select(p => Path.Combine(folder, p)))
                .Concat((render.Elements ?? []).SelectMany(e => new[] { e.Code?.File, e.Site?.File }))
                .OfType<string>().Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizeCodePath)
                .Where(p => InCodeProject(folder, p) && (SameCodePath(p, codePath) || File.Exists(p)))
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(p => new SourceFile(p, Path.GetRelativePath(NormalizeCodePath(folder), p))).ToArray()
            : [];
        if (!SourcePicker.Items.Cast<SourceFile>().SequenceEqual(paths))
        {
            syncingCode = true;
            try { SourcePicker.ItemsSource = paths; }
            finally { syncingCode = false; }
        }
        SelectCodePicker();
        UpdateCodeControls();
    }

    private static bool InCodeProject(string folder, string path)
    {
        string relative = Path.GetRelativePath(NormalizeCodePath(folder), NormalizeCodePath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private bool OpenCodeFile(string path, bool force = false)
    {
        if (project is not { } folder || string.IsNullOrWhiteSpace(path) || !InCodeProject(folder, path)) return false;
        path = NormalizeCodePath(path);
        if (SameCodePath(codePath, path) && !force) return true;
        if (!force && !ConfirmDiscardCode()) return false;
        try
        {
            LoadCode(path, NormalizeSource(File.ReadAllText(path)));
            RefreshCodeFiles();
            return true;
        }
        catch (Exception ex) { Status.Text = ex.Message; return false; }
    }

    private void LoadCode(string? path, string text)
    {
        syncingCode = true;
        try
        {
            codePath = path is null ? null : NormalizeCodePath(path);
            codeOriginal = text;
            SourceText.Text = text;
            SourceText.Document.UndoStack.ClearAll();
            SourceText.SyntaxHighlighting = path is null ? null : HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(path));
        }
        finally { syncingCode = false; }
        UpdateCodeControls();
    }

    private void RefreshCode(FigureRender render, string source)
    {
        if (!SameCodePath(codeFigurePath, render.File))
        {
            codeFigurePath = NormalizeCodePath(render.File);
            LoadCode(render.File, source);
        }
        else if (!CodeDirty && codePath is { } path)
        {
            try
            {
                string disk = SameCodePath(path, render.File) ? source : NormalizeSource(File.ReadAllText(path));
                if (disk != codeOriginal)
                {
                    int offset = SourceText.CaretOffset;
                    double scroll = SourceText.VerticalOffset;
                    LoadCode(path, disk);
                    SourceText.CaretOffset = Math.Min(offset, disk.Length);
                    SourceText.ScrollToVerticalOffset(scroll);
                }
            }
            catch (IOException) { /* Keep the last contents visible; explicit reload reports the error. */ }
        }
        RefreshCodeFiles();
    }

    private void ClearCode()
    {
        codeFigurePath = null;
        renderedSource = "";
        LoadCode(null, "");
        RefreshCodeFiles();
    }

    private async void EditSourceClicked(object sender, RoutedEventArgs e) => await SaveCode();

    private void RevertCodeClicked(object sender, RoutedEventArgs e)
    {
        if (codePath is { } path && ConfirmDiscardCode()) OpenCodeFile(path, force: true);
    }

    private void ExternalCodeClicked(object sender, RoutedEventArgs e)
    {
        if (codePath is not { } path || !File.Exists(path)) return;
        try
        {
            // Opening an associated .py file may execute it on Windows, so invoke an editor explicitly.
            var start = new ProcessStartInfo(ExternalEditorPath());
            start.ArgumentList.Add(path);
            using var editor = Process.Start(start);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private string EditorFile => Path.Combine(settingsDirectory, "editor.json");
    private string ExternalEditorPath()
    {
        try
        {
            if (File.Exists(EditorFile) && System.Text.Json.JsonSerializer.Deserialize<string>(File.ReadAllText(EditorFile)) is { } path &&
                Path.IsPathFullyQualified(path) && File.Exists(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
    }

    private void ChooseExternalEditor()
    {
        var pick = new Microsoft.Win32.OpenFileDialog { Title = "Choose your external editor", Filter = "Applications|*.exe", CheckFileExists = true };
        if (pick.ShowDialog(this) != true) return;
        try { SaveSetting(EditorFile, pick.FileName); Status.Text = "External editor: " + pick.FileName; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private void RevealSource()
    {
        if (codePath is not { } path || !File.Exists(path)) return;
        RevealFile(path);
    }

    private void RevealFile(string path)
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
            start.ArgumentList.Add("/select," + path);
            using var explorer = Process.Start(start);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async Task<bool> SaveCode()
    {
        if (busy || project is null || codePath is not { } path || !CodeDirty) return false;
        string draft = NormalizeSource(SourceText.Text);
        bool saved = false;
        await During("Applying source changes…", async () =>
        {
            await ApplySource(path, codeOriginal, draft);
            codeOriginal = draft;
            saved = true;
            await RefreshProject(shownFigure, external: true);
        });
        return saved;
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || closeAfterApply) return;
        if (!ConfirmDiscardCaption() || !ConfirmCloseShells()) { e.Cancel = true; return; }
        if (!CodeDirty) return;
        var answer = MessageBox.Show(this, "Apply unsaved code before closing?\n\nYes: Apply · No: Discard · Cancel: Keep editing",
            "Unsaved code", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.No) return;
        e.Cancel = true;
        if (answer != MessageBoxResult.Yes || !await SaveCode()) return;
        closeAfterApply = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
