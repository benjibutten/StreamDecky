using StreamDecky.Services;
using StreamDecky.ViewModels;
using StreamDecky.Views;
using Xunit;

namespace StreamDecky.Tests;

public sealed class OverlayWindowXamlSmokeTests
{
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
