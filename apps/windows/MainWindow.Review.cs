using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private string? captionPath;
    private string captionOriginal = "";
    private bool refreshingHistory;
    private bool CaptionDirty => captionPath is not null && NormalizeSource(CaptionText.Text) != captionOriginal;

    private void ShowCaption()
    {
        ShowDrawer(CaptionTab);
        CaptionText.Focus();
    }

    private void RefreshCaption(FigureRender? render)
    {
        if (!SameCodePath(captionPath, render?.File) || !CaptionDirty)
        {
            captionPath = render?.File;
            captionOriginal = NormalizeSource(render?.Caption?.Text ?? "");
            if (CaptionText.Text != captionOriginal) CaptionText.Text = captionOriginal;
        }
        UpdateReviewControls();
    }

    private void UpdateReviewControls()
    {
        if (HistoryJumpButton is null || CaptionText is null || HistoryList is null) return;
        CaptionText.IsReadOnly = busy || !validFigure;
        SaveCaptionButton.IsEnabled = !busy && validFigure && CaptionDirty;
        HistoryJumpButton.IsEnabled = !busy && HistoryList.SelectedItem is HistoryStep { Current: false };
    }

    private void CaptionTextChanged(object sender, TextChangedEventArgs e) => UpdateReviewControls();
    private async void SaveCaptionClicked(object sender, RoutedEventArgs e) => await SaveCaption();

    private async Task SaveCaption()
    {
        if (busy || !validFigure || !CaptionDirty || shownFigure is not { } figure) return;
        string draft = NormalizeSource(CaptionText.Text);
        await ApplyCaption(figure, captionOriginal, draft);
        if (NormalizeSource(currentRender?.Caption?.Text ?? "") == draft) captionOriginal = draft;
        UpdateReviewControls();
    }

    private bool ConfirmDiscardCaption() => !CaptionDirty || MessageBox.Show(this,
        "Discard unsaved changes to the figure caption? Choose Cancel to keep editing or apply them first.",
        "Unsaved caption", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private Task ShowHistory()
    {
        ShowDrawer(HistoryTab);
        HistoryList.Focus();
        return Task.CompletedTask;
    }

    private void RefreshHistory()
    {
        string[] previous = state?.History ?? [], next = state?.Future ?? [];
        HistoryStep[] steps = state is null ? [] : previous.Prepend("Project opened").Concat(next)
            .Select((label, index) => new HistoryStep(index, label, index == previous.Length)).Reverse().ToArray();
        if (HistoryList.Items.Cast<HistoryStep>().SequenceEqual(steps)) return;
        refreshingHistory = true;
        try
        {
            HistoryList.ItemsSource = steps;
            HistoryList.SelectedItem = steps.FirstOrDefault(s => s.Current);
            if (HistoryList.SelectedItem is { } selected) HistoryList.ScrollIntoView(selected);
        }
        finally { refreshingHistory = false; }
        UpdateReviewControls();
    }

    private void HistorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingHistory) UpdateReviewControls();
    }

    private async void JumpHistoryClicked(object sender, RoutedEventArgs e) => await JumpToSelectedHistory();
    private async void HistoryDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(HistoryList, e.OriginalSource as DependencyObject) is ListBoxItem)
            await JumpToSelectedHistory();
    }

    private Task JumpToSelectedHistory() => HistoryList.SelectedItem is HistoryStep { Current: false } step
        ? JumpHistory(step.Index) : Task.CompletedTask;
}
