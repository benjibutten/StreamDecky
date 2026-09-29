using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using StreamDecky.Admin;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;

namespace StreamDecky.Helpers;

/// <summary>The release notes of the running version, shown once after an update.</summary>
internal static class WhatsNewDialog
{
    private const string ReleaseNotesResource = "StreamDecky.RELEASE_NOTES.md";
    private const string ReleasesUrl = "https://github.com/benjibutten/StreamDecky/releases";

    private static readonly Brush Ink = BrushFrom("#F1F1F7");
    private static readonly Brush MutedInk = BrushFrom("#A8A8BE");
    private static readonly Brush Accent = BrushFrom("#B4A7FF");

    public static void Show(Window owner)
    {
        var window = new Window
        {
            Title = "What's new in StreamDecky",
            Width = 540,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = BrushFrom("#1E1E2E"),
            Foreground = Ink,
            Owner = owner
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "What's new in StreamDecky",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink
        });
        content.Children.Add(new TextBlock
        {
            Text = $"You now have {AppVersion.DisplayText}.",
            Margin = new Thickness(0, 2, 0, 14),
            FontSize = 11.5,
            Foreground = MutedInk
        });

        var list = new StackPanel();
        foreach (string item in ReadBuiltInItems())
            list.Children.Add(CreateItem(item));

        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = list
        });

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var releasesLink = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11.5 };
        var hyperlink = new Hyperlink(new Run("Earlier releases on GitHub"))
        {
            NavigateUri = new Uri(ReleasesUrl),
            Foreground = Accent,
            FontWeight = FontWeights.SemiBold
        };
        hyperlink.RequestNavigate += (_, e) =>
        {
            ShellLauncher.Open(e.Uri.AbsoluteUri);
            e.Handled = true;
        };
        releasesLink.Inlines.Add(hyperlink);
        footer.Children.Add(releasesLink);

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

    /// <summary>
    /// True when the notes of <paramref name="current"/> have not been shown yet and this
    /// is an update, not a first run. Development builds never show them.
    /// </summary>
    public static bool ShouldShow(Version? current, string? shownForVersion, bool isSetUp) =>
        current is { Major: >= 2000 }
        && isSetUp
        && (!Version.TryParse(shownForVersion, out Version? shown) || shown < current);

    /// <summary>The items of the release notes built into this version; empty when there are none.</summary>
    public static IReadOnlyList<string> ReadBuiltInItems()
    {
        using Stream? stream = typeof(WhatsNewDialog).Assembly.GetManifestResourceStream(ReleaseNotesResource);
        if (stream == null)
            return [];

        using var reader = new StreamReader(stream);
        return ParseItems(reader.ReadToEnd());
    }

    /// <summary>
    /// The top-level list items of RELEASE_NOTES.md, each joined onto one line with its
    /// <c>**bold**</c> markers kept. Headings and comments are left out.
    /// </summary>
    internal static IReadOnlyList<string> ParseItems(string markdown)
    {
        var items = new List<string>();
        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("- ", StringComparison.Ordinal))
                items.Add(line[2..].Trim());
            else if (items.Count > 0 && line.StartsWith("  ", StringComparison.Ordinal) && line.Trim().Length > 0)
                items[^1] += " " + line.Trim();
        }

        return items;
    }

    private static TextBlock CreateItem(string item)
    {
        var text = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 19,
            Foreground = Ink
        };
        text.Inlines.Add(new Run("•  ") { Foreground = Accent });

        // Every other piece between ** markers is bold.
        string[] pieces = item.Split("**");
        for (int index = 0; index < pieces.Length; index++)
        {
            var run = new Run(pieces[index]);
            text.Inlines.Add(index % 2 == 1 ? new Bold(run) : run);
        }

        return text;
    }

    private static Brush BrushFrom(string color) =>
        (Brush)new BrushConverter().ConvertFromString(color)!;
}
