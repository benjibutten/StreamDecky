using System.Linq;
using StreamDecky.Services;
using StreamDecky.ViewModels;
using StreamDecky.Views;
using Xunit;

namespace StreamDecky.Tests;

public sealed class OverlayWindowXamlSmokeTests
{
    [Fact]
    public void OverlayFilters_AreIndependentAndAllowMultipleTags()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var tempDirectory = new TemporaryDirectory();
                using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
                var firstCollection = viewModel.QuickTextCollections[0];
                var firstTag = viewModel.QuickTextCategories[0];

                viewModel.AddQuickTextItemCommand.Execute(null);
                viewModel.QuickTextItems[^1].Text = "First tag";

                viewModel.AddQuickTextCategoryCommand.Execute(null);
                var secondTag = viewModel.QuickTextCategories[^1];
                viewModel.AddQuickTextItemCommand.Execute(null);
                viewModel.QuickTextItems[^1].Text = "Second tag";

                viewModel.SelectedQuickTextCategoryId = firstTag.Id;
                viewModel.AddQuickTextCollectionCommand.Execute(null);
                var secondCollection = viewModel.QuickTextCollections[^1];
                viewModel.AddQuickTextItemCommand.Execute(null);
                viewModel.QuickTextItems[^1].Text = "Second collection";
                viewModel.SelectedQuickTextCollectionId = firstCollection.Id;

                var window = new OverlayWindow(viewModel);
                try
                {
                    Assert.Single(window.OverlayQuickTextItems);

                    var secondTagFilter = Assert.Single(
                        window.OverlayQuickTextCategories,
                        filter => filter.Category.Id == secondTag.Id);
                    secondTagFilter.IsSelected = true;

                    Assert.Equal(2, window.OverlayQuickTextItems.Count);
                    Assert.Equal(firstTag.Id, viewModel.SelectedQuickTextCategoryId);

                    window.ClearOverlayQuickTextTags();

                    Assert.Empty(window.OverlaySelectedQuickTextCategoryIds);
                    Assert.All(window.OverlayQuickTextCategories, filter => Assert.False(filter.IsSelected));
                    Assert.Equal(2, window.OverlayQuickTextItems.Count);
                    Assert.Equal(firstTag.Id, viewModel.SelectedQuickTextCategoryId);

                    window.OverlaySelectedQuickTextCollectionId = secondCollection.Id;

                    Assert.Single(window.OverlayQuickTextItems);
                    Assert.Equal("Second collection", window.OverlayQuickTextItems[0].SessionText);
                    Assert.Equal(firstCollection.Id, viewModel.SelectedQuickTextCollectionId);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The overlay filter test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void OverlaySearch_DoesNotFilterEditorState()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var tempDirectory = new TemporaryDirectory();
                using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
                viewModel.RenameQuickTextCategoryCommand.Execute("Support");
                for (int index = 0; index < 3; index++)
                {
                    viewModel.AddQuickTextItemCommand.Execute(null);
                    viewModel.QuickTextItems[^1].Text = $"Support text {index + 1}";
                }
                int editorItemCount = viewModel.QuickTextItems.Count;

                var window = new OverlayWindow(viewModel);
                try
                {
                    window.OverlayQuickTextSearchQuery = "Support";

                    Assert.Equal(string.Empty, viewModel.QuickTextSearchQuery);
                    Assert.Equal(editorItemCount, viewModel.QuickTextItems.Count);
                    Assert.Equal(3, window.OverlayQuickTextItems.Count);
                    Assert.All(window.OverlayQuickTextItems, item => Assert.Contains("Support", item.CategoryName));
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The overlay search test timed out.");
        Assert.Null(failure);
    }

    /// <summary>
    /// Inflates the full overlay visual tree (including the MicMixer music widget)
    /// so template/resource errors in OverlayWindow.xaml fail here instead of at
    /// the first hotkey press.
    /// </summary>
    [Fact]
    public void OverlayWindow_ShouldInflateXamlWithMusicWidget()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var tempDirectory = new TemporaryDirectory();
                using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
                viewModel.MusicWidgetVisible = true;
                viewModel.AddQuickTextItemCommand.Execute(null);
                viewModel.QuickTextItems[0].Text = "Clipboard smoke test";

                // Make every widget section visible and populated so the item
                // DataTemplates (tracks, queue, folder chips) are inflated too.
                MusicWidgetViewModel widget = viewModel.MusicWidget;
                widget.IsConnected = true;
                widget.ShowPlayerControls = true;
                widget.ShowFolderFilter = true;
                widget.ShowRoutingHint = true;
                widget.HasQueue = true;
                widget.HasTracks = true;
                widget.HasMoreTracks = true;
                widget.PlaybackBlockedReason = "Blocked reason";
                widget.ErrorMessage = "Error message";
                widget.StatusText = "Status";
                widget.Folders.Add(new MusicFolderOptionViewModel(null, "All") { IsSelected = true });
                widget.Folders.Add(new MusicFolderOptionViewModel("f1", "Music"));
                widget.Queue.Add(new MusicQueueItemViewModel(0, "t2", "Queued track"));
                widget.Tracks.Add(new MusicTrackItemViewModel("t1", "Track one", "Music")
                {
                    IsCurrent = true,
                    QueueBadgeText = "Queued #1"
                });

                var window = new OverlayWindow(viewModel);
                try
                {
                    // Force template application and layout of the whole tree.
                    window.Measure(new System.Windows.Size(1920, 1080));
                    window.Arrange(new System.Windows.Rect(0, 0, 1920, 1080));
                    window.UpdateLayout();
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The overlay window smoke test timed out.");
        Assert.Null(failure);
    }

    /// <summary>
    /// Inflates the text helper widget and reads the values back off the live
    /// controls, so a mistyped binding path fails here rather than showing up as a
    /// silently empty box in the overlay.
    /// </summary>
    [Fact]
    public void OverlayWindow_ShouldBindTheTextHelperWidget()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var tempDirectory = new TemporaryDirectory();
                using var viewModel = new MainViewModel(
                    new ProfileService(tempDirectory.Path),
                    appSettingsService: new AppSettingsService(tempDirectory.Path));

                viewModel.TextHelperVisible = true;
                viewModel.TextHelperFontFamily = "Tahoma";
                viewModel.TextHelperFontSize = 22;
                viewModel.TextHelperWidget.Text = "helo wrld";
                viewModel.TextHelperWidget.StatusText = "Status line";

                var window = new OverlayWindow(viewModel);
                try
                {
                    window.Measure(new System.Windows.Size(1920, 1080));
                    window.Arrange(new System.Windows.Rect(0, 0, 1920, 1080));
                    window.UpdateLayout();

                    Assert.Equal(System.Windows.Visibility.Visible, window.TextHelperPanel.Visibility);
                    Assert.Equal("helo wrld", window.TextHelperInput.Text);
                    Assert.Equal("Tahoma", window.TextHelperInput.FontFamily.Source);
                    Assert.Equal(22, window.TextHelperInput.FontSize);

                    // The widget writes back through the same two-way binding the user types into.
                    window.TextHelperInput.Text = "typed by hand";
                    Assert.Equal("typed by hand", viewModel.TextHelperWidget.Text);

                    // Ctrl+Enter respells and Ctrl+Shift+Enter asks; plain Enter stays a newline.
                    var shortcuts = window.TextHelperInput.InputBindings
                        .Cast<System.Windows.Input.KeyBinding>()
                        .ToList();
                    Assert.Equal(2, shortcuts.Count);
                    Assert.All(shortcuts, binding => Assert.Equal(System.Windows.Input.Key.Return, binding.Key));

                    var spellShortcut = Assert.Single(
                        shortcuts,
                        binding => binding.Modifiers == System.Windows.Input.ModifierKeys.Control);
                    Assert.Same(viewModel.TextHelperWidget.SpellCheckCommand, spellShortcut.Command);

                    var askShortcut = Assert.Single(
                        shortcuts,
                        binding => binding.Modifiers ==
                            (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));
                    Assert.Same(viewModel.TextHelperWidget.AskCommand, askShortcut.Command);

                    // The writing area defaults to the light theme and follows the profile switch.
                    Assert.Equal(
                        System.Windows.Media.Color.FromRgb(0x1B, 0x22, 0x2B),
                        ((System.Windows.Media.SolidColorBrush)window.TextHelperInput.Foreground).Color);

                    viewModel.TextHelperDarkTextArea = true;
                    window.UpdateLayout();

                    Assert.Equal(
                        System.Windows.Media.Color.FromRgb(0xEA, 0xF2, 0xFA),
                        ((System.Windows.Media.SolidColorBrush)window.TextHelperInput.Foreground).Color);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The text helper smoke test timed out.");
        Assert.Null(failure);
    }

    /// <summary>
    /// Inflates the quick answer card and reads it back off the live controls, so a
    /// mistyped binding shows up here rather than as an answer that silently never
    /// appears in the overlay.
    /// </summary>
    [Fact]
    public void OverlayWindow_ShouldBindTheQuickAnswerCard()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var tempDirectory = new TemporaryDirectory();
                using var viewModel = new MainViewModel(
                    new ProfileService(tempDirectory.Path),
                    appSettingsService: new AppSettingsService(tempDirectory.Path));

                viewModel.TextHelperVisible = true;
                TextHelperWidgetViewModel widget = viewModel.TextHelperWidget;
                widget.Text = "who won last night";
                widget.AskedQuestion = "who won last night";
                widget.AnswerText = "They won 3-1.";
                widget.AnswerUsedSearch = true;
                widget.AnswerSources.Add(new QuickAnswerSource("Race report", "https://sport.test/report", "Sport Test"));

                var window = new OverlayWindow(viewModel);
                try
                {
                    window.Measure(new System.Windows.Size(1920, 1080));
                    window.Arrange(new System.Windows.Rect(0, 0, 1920, 1080));
                    window.UpdateLayout();

                    Assert.True(widget.IsAnswerAreaVisible);
                    Assert.Equal("They won 3-1.", window.TextHelperAnswer.Text);

                    // The answer is read-only: it is not the box the user writes in.
                    Assert.True(window.TextHelperAnswer.IsReadOnly);

                    // The chip has to exist as a real, clickable control: a source the
                    // user cannot open is not a source they can check.
                    Assert.Single(window.TextHelperAnswerSources.Items);
                    window.TextHelperAnswerSources.Measure(new System.Windows.Size(400, 400));
                    window.TextHelperAnswerSources.Arrange(new System.Windows.Rect(0, 0, 400, 400));
                    window.TextHelperAnswerSources.UpdateLayout();
                    var chip = Assert.Single(
                        FindVisualChildren<System.Windows.Controls.Button>(window.TextHelperAnswerSources));
                    Assert.Equal("https://sport.test/report", chip.Tag);

                    // Dismissing takes the card away without touching the written text.
                    widget.DismissAnswerCommand.Execute(null);
                    window.UpdateLayout();

                    Assert.False(widget.IsAnswerAreaVisible);
                    Assert.Equal("who won last night", window.TextHelperInput.Text);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The quick answer smoke test timed out.");
        Assert.Null(failure);
    }

    /// <summary>
    /// At the narrowest allowed widget width the four actions do not fit on one line, so
    /// the row has to wrap. The alternative is the last button being clipped out of reach
    /// on exactly the widget size someone tucking this into a corner would pick.
    /// </summary>
    [Fact]
    public void TextHelperActions_WrapInsteadOfClippingAtTheNarrowestWidth()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var tempDirectory = new TemporaryDirectory();
                using var viewModel = new MainViewModel(
                    new ProfileService(tempDirectory.Path),
                    appSettingsService: new AppSettingsService(tempDirectory.Path));

                viewModel.TextHelperVisible = true;
                viewModel.TextHelperWidth = StreamDecky.Models.DeckProfile.MinTextHelperWidth;

                // Undo only appears after a correction, and it is the button most at risk.
                viewModel.TextHelperWidget.Text = "some text";
                viewModel.TextHelperWidget.CanUndoSpellCheck = true;

                var window = new OverlayWindow(viewModel);
                try
                {
                    window.Measure(new System.Windows.Size(1920, 1080));
                    window.Arrange(new System.Windows.Rect(0, 0, 1920, 1080));
                    window.UpdateLayout();

                    // Lay the widget out at the width the profile actually gives it, which
                    // is the constraint under test.
                    var widgetSize = new System.Windows.Size(
                        viewModel.TextHelperWidth,
                        viewModel.TextHelperHeight);
                    window.TextHelperPanel.Measure(widgetSize);
                    window.TextHelperPanel.Arrange(new System.Windows.Rect(widgetSize));
                    window.TextHelperPanel.UpdateLayout();

                    var buttons = FindVisualChildren<System.Windows.Controls.Button>(window.TextHelperActions)
                        .Where(button => button.ActualHeight > 0)
                        .ToList();
                    Assert.Equal(4, buttons.Count);

                    // Wrapping shows up as a row taller than a single button.
                    Assert.True(
                        window.TextHelperActions.ActualHeight > buttons[0].ActualHeight + 1,
                        $"The action row did not wrap: {window.TextHelperActions.ActualHeight} high "
                        + $"for buttons of {buttons[0].ActualHeight}.");

                    // Nothing may stick out past the panel it lives in.
                    Assert.True(
                        window.TextHelperActions.ActualWidth <= window.TextHelperPanel.ActualWidth,
                        "The action row is wider than the widget.");
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The action row smoke test timed out.");
        Assert.Null(failure);
    }

    /// <summary>
    /// Making room for the first answer grows the widget without the user touching it, so
    /// one parked near the bottom edge would otherwise grow straight off the screen.
    /// </summary>
    [Fact]
    public void GrowingTheTextHelperKeepsItOnScreen()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var tempDirectory = new TemporaryDirectory();
                using var viewModel = new MainViewModel(
                    new ProfileService(tempDirectory.Path),
                    appSettingsService: new AppSettingsService(tempDirectory.Path));

                viewModel.TextHelperVisible = true;
                viewModel.TextHelperHeight = StreamDecky.Models.DeckProfile.MinTextHelperHeight;

                var window = new OverlayWindow(viewModel);
                try
                {
                    window.Measure(new System.Windows.Size(1920, 1080));
                    window.Arrange(new System.Windows.Rect(0, 0, 1920, 1080));
                    window.UpdateLayout();

                    // Park it far past the bottom edge. Moving it does not re-clamp on its
                    // own, so this stands until something else triggers one.
                    viewModel.TextHelperY = 100_000;
                    FlushDispatcher(window);
                    double parkedY = viewModel.TextHelperY;
                    Assert.Equal(100_000, parkedY);

                    // Growing the way the first answer does has to pull it back in.
                    viewModel.TextHelperHeight = 460;
                    FlushDispatcher(window);

                    Assert.True(
                        viewModel.TextHelperY < parkedY,
                        $"Growing should have re-clamped the widget, but Y stayed at {viewModel.TextHelperY}.");

                    // The window is never shown here, so its bounds are whatever layout
                    // gave it; the invariant is checked against those rather than a
                    // guessed screen size.
                    double maxY = Math.Max(0, window.ActualHeight - viewModel.TextHelperHeight - 12);
                    Assert.True(
                        viewModel.TextHelperY <= maxY,
                        $"The widget runs off the bottom: Y {viewModel.TextHelperY} exceeds the limit of {maxY}.");
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The text helper clamp test timed out.");
        Assert.Null(failure);
    }

    /// <summary>Runs everything queued at or above Loaded priority, then re-lays out.</summary>
    private static void FlushDispatcher(System.Windows.Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(
            () => { },
            System.Windows.Threading.DispatcherPriority.Loaded);
        window.UpdateLayout();
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        for (int index = 0; index < count; index++)
        {
            System.Windows.DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);

            if (child is T match)
                yield return match;

            foreach (T descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
