using System.Windows;
using System.Windows.Controls;

namespace StreamDecky.Helpers;

public static class ConfirmDialog
{
    public static bool Show(
        Window? owner,
        string title,
        string message,
        string confirmText = "Yes",
        string cancelText = "Cancel",
        bool danger = false)
    {
        var window = new Window
        {
            Title = title,
            Width = 440,
            MinWidth = 440,
            SizeToContent = SizeToContent.Height,
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

        var messagePanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };

        if (danger)
        {
            messagePanel.Children.Add(new TextBlock
            {
                Text = "⚠",
                FontSize = 24,
                Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E0A030")!,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Top
            });
        }

        messagePanel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = System.Windows.Media.Brushes.Gainsboro,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 350,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });

        Grid.SetRow(messagePanel, 0);
        root.Children.Add(messagePanel);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = cancelText,
            MinWidth = 80,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#3A3A52")!,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            IsCancel = true
        };

        var confirmButton = new System.Windows.Controls.Button
        {
            Content = confirmText,
            MinWidth = 80,
            Padding = new Thickness(10, 5, 10, 5),
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(danger ? "#5A2727" : "#2D5A27")!,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };

        cancelButton.Click += (_, _) =>
        {
            window.DialogResult = false;
            window.Close();
        };

        confirmButton.Click += (_, _) =>
        {
            window.DialogResult = true;
            window.Close();
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        window.Content = root;
        window.Loaded += (_, _) => cancelButton.Focus();

        return window.ShowDialog() == true;
    }
}
