using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Fignimble.Windows;

public partial class NewFigureDialog : Window
{
    public string FigureName { get; private set; } = "fig";
    public double FigureWidth { get; private set; }
    public double FigureHeight { get; private set; }

    private sealed record FigureTemplateChoice(string? Key, string Label, double[]? Size)
    {
        public override string ToString() => Label;
    }
    public string? PosterTemplate { get; private set; }
    private string blankWidth = "", blankHeight = "";

    public NewFigureDialog(double[]? size) : this(size, []) { }

    internal NewFigureDialog(double[]? size, IEnumerable<(string Key, string Label, double[] Size)> posters)
    {
        InitializeComponent();
        InputWidth.Text = blankWidth = MainWindow.FormatMm(size is [var width, _] ? width : 183);
        InputHeight.Text = blankHeight = MainWindow.FormatMm(size is [_, var height] ? height : 100);
        InputTemplate.ItemsSource = new[] { new FigureTemplateChoice(null, "Blank figure", null) }
            .Concat(posters.Select(p => new FigureTemplateChoice(p.Key, p.Label, p.Size))).ToArray();
        InputTemplate.SelectedIndex = 0;
        Loaded += (_, _) => { InputName.Focus(); InputName.SelectAll(); };
    }

    private void TemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputWidth is null || InputHeight is null) return;
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is FigureTemplateChoice { Key: null })
        { blankWidth = InputWidth.Text; blankHeight = InputHeight.Text; }
        if (InputTemplate.SelectedItem is FigureTemplateChoice { Key: { }, Size: [var width, var height] })
        {
            InputWidth.Text = MainWindow.FormatMm(width); InputHeight.Text = MainWindow.FormatMm(height);
            InputWidth.IsEnabled = InputHeight.IsEnabled = false;
        }
        else
        {
            InputWidth.Text = blankWidth; InputHeight.Text = blankHeight;
            InputWidth.IsEnabled = InputHeight.IsEnabled = true;
        }
    }

    private void CreateClicked(object sender, RoutedEventArgs e)
    {
        string name = InputName.Text.Trim();
        if (!Regex.IsMatch(name, @"\A[a-zA-Z_][a-zA-Z_0-9]{0,63}\z"))
        {
            ErrorText.Text = "Use 1–64 letters, digits, or underscores, starting with a letter or underscore.";
            return;
        }
        if (!MainWindow.TryMm(InputWidth.Text, true, out double width) ||
            !MainWindow.TryMm(InputHeight.Text, true, out double height))
        {
            ErrorText.Text = "Width and height must be positive numbers in millimeters.";
            return;
        }
        FigureName = name;
        PosterTemplate = (InputTemplate.SelectedItem as FigureTemplateChoice)?.Key;
        FigureWidth = width;
        FigureHeight = height;
        DialogResult = true;
    }
}
