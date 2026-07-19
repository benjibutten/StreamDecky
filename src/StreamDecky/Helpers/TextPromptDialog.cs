using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StreamDecky.Helpers;

public static class TextPromptDialog
{
    public static string? Show(
        Window? owner,
        string title,
        string prompt,
        string initialValue,
        int maxLength = 80)
    {
        var window = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            MinWidth = 420,
            MinHeight = 180,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1E1E2E")!,
            Foreground = System.Windows.Media.Brushes.White,
            Owner = owner
        };

        var root = new Grid
        {
            Margin = new Thickness(14)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var promptBlock = new TextBlock
        {
            Text = prompt,
            Foreground = System.Windows.Media.Brushes.Gainsboro,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(promptBlock, 0);
        root.Children.Add(promptBlock);

        var inputBox = new System.Windows.Controls.TextBox
        {
            Text = initialValue,
            MaxLength = Math.Max(1, maxLength),
            Padding = new Thickness(8, 5, 8, 5),
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#252536")!,
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#4A4A66")!,
            BorderThickness = new Thickness(1)
        };
        Grid.SetRow(inputBox, 1);
        root.Children.Add(inputBox);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#3A3A52")!,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "Rename",
            MinWidth = 80,
            Padding = new Thickness(10, 5, 10, 5),
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#2D5A27")!,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };

        string? result = null;

        cancelButton.Click += (_, _) =>
        {
            window.DialogResult = false;
            window.Close();
        };

        okButton.Click += (_, _) =>
        {
            string value = inputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                System.Windows.MessageBox.Show(window,
                    "Name cannot be empty.",
                    title,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                inputBox.Focus();
                inputBox.SelectAll();
                return;
            }

            result = value;
            window.DialogResult = true;
            window.Close();
        };

        inputBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                okButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                cancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                e.Handled = true;
            }
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        window.Content = root;
        window.Loaded += (_, _) =>
        {
            inputBox.Focus();
            inputBox.SelectAll();
        };

        bool? accepted = window.ShowDialog();
        return accepted == true ? result : null;
    }
}
