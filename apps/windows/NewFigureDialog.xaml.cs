using System.Text.RegularExpressions;
using System.Windows;

namespace Fignimble.Windows;

public partial class NewFigureDialog : Window
{
    public string FigureName { get; private set; } = "fig";
    public double FigureWidth { get; private set; }
    public double FigureHeight { get; private set; }

    public NewFigureDialog(double[]? size)
    {
        InitializeComponent();
        InputWidth.Text = MainWindow.FormatMm(size is [var width, _] ? width : 183);
        InputHeight.Text = MainWindow.FormatMm(size is [_, var height] ? height : 100);
        Loaded += (_, _) => { InputName.Focus(); InputName.SelectAll(); };
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
        FigureWidth = width;
        FigureHeight = height;
        DialogResult = true;
    }
}
