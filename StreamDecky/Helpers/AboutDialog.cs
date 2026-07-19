using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace StreamDecky.Helpers;

internal static class AboutDialog
{
    private static readonly Brush Ink = BrushFrom("#F1F1F7");
    private static readonly Brush MutedInk = BrushFrom("#A8A8BE");
    private static readonly Brush Accent = BrushFrom("#B4A7FF");

    public static void Show(Window owner, string appName, string sourceUrl)
    {
        var window = new Window
        {
            Title = $"About {appName}",
            Width = 500,
            MinWidth = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = BrushFrom("#1E1E2E"),
            Foreground = Ink,
            Owner = owner
        };

        var content = new StackPanel();
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        heading.Children.Add(new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = BrushFrom("#353052"),
            Child = new TextBlock
            {
                Text = "i",
                FontFamily = new FontFamily("Georgia"),
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        var titlePanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"About {appName}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Open source, developed for real-world needs",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 11.5,
            Foreground = MutedInk
        });
        Grid.SetColumn(titlePanel, 1);
        heading.Children.Add(titlePanel);
        content.Children.Add(heading);

        content.Children.Add(CreateCard(
            "Free and open-source software",
            $"{appName} is provided as open-source software under the Apache License 2.0. " +
            "You may use, study, modify and share the code under the license terms.",
            "#25253A",
            "#41415A"));

        content.Children.Add(CreateSupportCard());

        content.Children.Add(CreateCard(
            "Third-party software",
            "StreamDecky uses CommunityToolkit.Mvvm (MIT) and Material Design Icons (Apache-2.0). " +
            "Complete attribution details are included with the distribution. The optional MicMixer " +
            "connection is an integration between two separate applications, not a bundled dependency.",
            "#25253A",
            "#41415A"));

        var footer = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sourceLink = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11.5
        };
        var hyperlink = new Hyperlink(new Run("View the source code on GitHub"))
        {
            NavigateUri = new Uri(sourceUrl),
            Foreground = Accent,
            FontWeight = FontWeights.SemiBold
        };
        hyperlink.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        sourceLink.Inlines.Add(hyperlink);
        footer.Children.Add(sourceLink);

        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 86,
            Padding = new Thickness(14, 6, 14, 6),
            Background = BrushFrom("#6C5CE7"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            IsDefault = true,
            IsCancel = true
        };
        closeButton.Click += (_, _) => window.Close();
        Grid.SetColumn(closeButton, 1);
        footer.Children.Add(closeButton);
        content.Children.Add(footer);

        window.Content = new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(12),
            Background = BrushFrom("#242438"),
            BorderBrush = BrushFrom("#41415A"),
            BorderThickness = new Thickness(1),
            Child = content
        };
        window.Loaded += (_, _) => closeButton.Focus();
        window.ShowDialog();
    }

    private static Border CreateCard(string title, string body, string background, string border)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
            Margin = new Thickness(0, 0, 0, 5)
        });
        panel.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            LineHeight = 18,
            Foreground = MutedInk
        });

        return new Border
        {
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(8),
            Background = BrushFrom(background),
            BorderBrush = BrushFrom(border),
            BorderThickness = new Thickness(1),
            Child = panel
        };
    }

    private static Border CreateSupportCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Development and a special thank you",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink,
            Margin = new Thickness(0, 0, 0, 5)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "StreamDecky is developed and maintained by BenjiButten and is the result of many " +
                   "hours of development. The app is provided free of charge as open-source software. " +
                   "Pixlexi has contributed use cases, hands-on testing and valuable feedback throughout development.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            LineHeight = 18,
            Foreground = MutedInk
        });

        var supportText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            LineHeight = 18,
            Foreground = MutedInk
        };
        supportText.Inlines.Add(new Run("If you enjoy the app and would like to give something back, please support "));
        var pixlexiLink = new Hyperlink(new Run("Pixlexi"))
        {
            NavigateUri = new Uri("https://www.twitch.tv/pixlexi"),
            Foreground = Accent,
            FontWeight = FontWeights.SemiBold
        };
        pixlexiLink.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        supportText.Inlines.Add(pixlexiLink);
        supportText.Inlines.Add(new Run(" by gifting a sub on Twitch."));
        panel.Children.Add(supportText);

        return new Border
        {
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(8),
            Background = BrushFrom("#2B2742"),
            BorderBrush = BrushFrom("#514979"),
            BorderThickness = new Thickness(1),
            Child = panel
        };
    }

    private static Brush BrushFrom(string color) =>
        (Brush)new BrushConverter().ConvertFromString(color)!;
}
