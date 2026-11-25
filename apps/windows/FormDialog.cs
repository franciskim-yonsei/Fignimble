using System.Windows;
using System.Windows.Controls;

namespace Fignimble.Windows;

/// <summary>Small task-specific forms; validation runs before the dialog closes.</summary>
internal sealed class FormDialog : Window
{
    private readonly StackPanel fields = new() { Margin = new Thickness(14) };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };

    public FormDialog(Window owner, string title)
    {
        Owner = owner;
        Title = title;
        Width = 390;
        MaxHeight = SystemParameters.WorkArea.Height;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = fields };
    }

    public TextBox Text(string label, string value, bool multiline = false)
    {
        var box = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 76 : 0 };
        Add(label, box);
        return box;
    }

    public ComboBox Choice<T>(string label, IEnumerable<T> items, T current)
    {
        var box = new ComboBox { ItemsSource = items, SelectedItem = current };
        Add(label, box);
        return box;
    }

    public CheckBox Check(string label, bool value = false)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 2, 0, 8) };
        fields.Children.Add(box);
        return box;
    }

    public void Note(string text) => fields.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8) });

    public void Add(string label, Control control)
    {
        fields.Children.Add(new Label { Content = label, Target = control, Padding = new Thickness(0, 0, 0, 3) });
        control.Margin = new Thickness(0, 0, 0, 8);
        fields.Children.Add(control);
    }

    public bool Confirm(string action, Func<string?> validate) => ConfirmAsync(action, () => Task.FromResult(validate()));

    public bool ConfirmAsync(string action, Func<Task<string?>> apply)
    {
        fields.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = action, IsDefault = true, MinWidth = 75, Padding = new Thickness(8, 4, 8, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 75, Margin = new Thickness(6, 0, 0, 0) };
        ok.Click += async (_, _) =>
        {
            fields.IsEnabled = false;
            try
            {
                string? message = await apply();
                if (message is null) DialogResult = true;
                else error.Text = message;
            }
            catch (Exception ex) { error.Text = ex.Message; }
            finally { fields.IsEnabled = true; }
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        fields.Children.Add(buttons);
        return ShowDialog() == true;
    }
}
