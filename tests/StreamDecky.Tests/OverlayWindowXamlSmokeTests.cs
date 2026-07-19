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
}
