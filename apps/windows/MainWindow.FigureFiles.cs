using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private const string FigureClipboardFormat = "Fignimble.FigureFile";
    private sealed record FigureFile(string Name, string Text, Dictionary<string, string> Data);
    private sealed record SourceDataExport(string Path, SourceDataPanel[] Panels);
    private sealed record SourceDataPanel(string Panel, string How);
    private (string Token, FigureFile Figure)? copiedFigure;

    private void SetupFigureFiles()
    {
        var menu = new ContextMenu();
        Figures.ContextMenu = menu;
        Figures.ContextMenuOpening += (_, e) =>
        {
            if (busy || project is null) { e.Handled = true; return; }
            var card = e.OriginalSource is DependencyObject element
                ? (ItemsControl.ContainerFromElement(Figures, element) as ListBoxItem)?.Content as FigureCard : null;
            menu.Items.Clear();
            MenuAction(menu, "Cut Figure", () => RemoveFigureFile(card!.Name, cut: true), card is not null);
            MenuAction(menu, "Copy Figure", () => During("Copying figure source…", async () =>
            {
                await CopyFigureFile(card!.Name);
                Status.Text = $"Copied {card.Name}.";
            }), card is not null);
            MenuAction(menu, "Paste Figure", PasteFigureFile, FigureFromClipboard() is not null);
            MenuAction(menu, "Delete Figure", () => RemoveFigureFile(card!.Name, cut: false), card is not null);
            menu.Items.Add(new Separator());
            MenuAction(menu, "New Figure…", () => NewFigureClicked(this, new RoutedEventArgs()));
            MenuAction(menu, "New Table…", ChooseNewTable);
        };
    }

    private FigureFile? FigureFromClipboard()
    {
        if (copiedFigure is not { } copy) return null;
        try { return Clipboard.GetData(FigureClipboardFormat) is string token && token == copy.Token ? copy.Figure : null; }
        catch (System.Runtime.InteropServices.ExternalException) { return null; }
    }

    private async Task CopyFigureFile(string name)
    {
        var figure = await engine!.CallAsync<FigureFile>("copy_figure", new { name });
        string token = Guid.NewGuid().ToString("N");
        var data = new DataObject();
        data.SetText(figure.Text);
        data.SetData(FigureClipboardFormat, token);
        Clipboard.SetDataObject(data, copy: true);
        copiedFigure = (token, figure);
    }

    private Task PasteFigureFile() => During("Pasting figure…", async () =>
    {
        if (FigureFromClipboard() is not { } figure || !ConfirmFigureAction("Paste")) return;
        var result = await engine!.CallAsync<NewFigureResult>("paste_figure", figure);
        await RefreshProject(result.Name);
    });

    private Task RemoveFigureFile(string name, bool cut) => During(cut ? "Cutting figure…" : "Deleting figure…", async () =>
    {
        string action = cut ? "Cut" : "Delete";
        if (MessageBox.Show(this, $"{action} {name}? You can restore it with Undo.", $"{action} figure",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        if (shownFigure == name && !ConfirmFigureAction(action)) return;
        if (cut) await CopyFigureFile(name);
        int index = figureCards.FindIndex(card => card.Name == name);
        string? next = shownFigure == name
            ? figureCards.Skip(index + 1).FirstOrDefault()?.Name ?? figureCards.Take(index).LastOrDefault()?.Name
            : shownFigure;
        var result = await engine!.CallAsync<DeletedFigure>("delete_figure", new { name });
        await RefreshProject(next);
        Status.Text = (cut ? "Cut " : "Deleted ") + name + ". Undo restores it." +
            (result.UsedBy.Length == 0 ? "" : $" Referenced by {string.Join(", ", result.UsedBy)}.");
    });

    private sealed record DeletedFigure(string[] UsedBy);

    private Task ChooseSourceDataExport()
    {
        if (busy || !validFigure || shownFigure is not { } figure) return Task.CompletedTask;
        var dialog = new SaveFileDialog { Title = "Export source data", Filter = "Excel workbook|*.xlsx",
            DefaultExt = ".xlsx", FileName = figure + "_source_data.xlsx", InitialDirectory = project };
        if (dialog.ShowDialog(this) != true) return Task.CompletedTask;
        return During("Exporting source data…", async () =>
        {
            var result = await engine!.CallAsync<SourceDataExport>("export_source_data", new { figure, path = dialog.FileName });
            var failed = result.Panels.Where(p => p.How == "failed").Select(p => p.Panel).ToArray();
            Status.Text = $"Exported source data to {result.Path}." +
                (failed.Length == 0 ? "" : $" See the workbook for panels that could not be exported ({string.Join(", ", failed)}).");
        });
    }
}
