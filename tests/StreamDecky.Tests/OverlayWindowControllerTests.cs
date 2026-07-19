using StreamDecky.Services;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class OverlayWindowControllerTests
{
    [Fact]
    public void Toggle_WhenClosed_OpensOverlayAndShowsWindow()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        var factory = new RecordingOverlayWindowFactory();
        var controller = new OverlayWindowController(viewModel, factory);

        controller.Toggle();

        Assert.True(viewModel.IsOverlayOpen);
        Assert.Equal(1, factory.CreateCallCount);
        Assert.NotNull(factory.LastWindow);
        Assert.Equal(1, factory.LastWindow!.ShowCallCount);
    }

    [Fact]
    public void Toggle_WhenOpen_HidesOverlayAndKeepsWindowAlive()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        var factory = new RecordingOverlayWindowFactory();
        var controller = new OverlayWindowController(viewModel, factory);

        controller.Open();
        controller.Toggle();

        Assert.False(viewModel.IsOverlayOpen);
        Assert.False(controller.IsOpen);
        Assert.Equal(1, factory.LastWindow!.HideCallCount);
    }

    [Fact]
    public void Toggle_AfterHide_ReusesExistingWindow()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        var factory = new RecordingOverlayWindowFactory();
        var controller = new OverlayWindowController(viewModel, factory);

        controller.Toggle();
        controller.Toggle();
        controller.Toggle();

        Assert.True(controller.IsOpen);
        Assert.True(viewModel.IsOverlayOpen);
        Assert.Equal(1, factory.CreateCallCount);
        Assert.Equal(2, factory.LastWindow!.ShowCallCount);
    }

    [Fact]
    public void Open_AfterWindowClosed_CreatesNewWindow()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        var factory = new RecordingOverlayWindowFactory();
        var controller = new OverlayWindowController(viewModel, factory);

        controller.Open();
        factory.LastWindow!.RaiseClosed();

        Assert.False(viewModel.IsOverlayOpen);
        Assert.False(controller.IsOpen);

        controller.Open();

        Assert.True(controller.IsOpen);
        Assert.Equal(2, factory.CreateCallCount);
    }

    private sealed class RecordingOverlayWindowFactory : IOverlayWindowFactory
    {
        public int CreateCallCount { get; private set; }

        public RecordingOverlayWindowHandle? LastWindow { get; private set; }

        public IOverlayWindowHandle Create(MainViewModel viewModel)
        {
            CreateCallCount++;
            LastWindow = new RecordingOverlayWindowHandle();
            return LastWindow;
        }
    }

    private sealed class RecordingOverlayWindowHandle : IOverlayWindowHandle
    {
        public event EventHandler? Closed;

        public bool IsOverlayVisible { get; private set; }

        public int ShowCallCount { get; private set; }

        public int HideCallCount { get; private set; }

        public void ShowOverlay()
        {
            ShowCallCount++;
            IsOverlayVisible = true;
        }

        public void HideOverlay()
        {
            HideCallCount++;
            IsOverlayVisible = false;
        }

        public void RaiseClosed()
        {
            IsOverlayVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
