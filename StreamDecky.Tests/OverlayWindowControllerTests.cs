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
    public void Toggle_WhenOpen_ClosesOverlayAndClearsWindowState()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        var factory = new RecordingOverlayWindowFactory();
        var controller = new OverlayWindowController(viewModel, factory);

        controller.Open();
        controller.Toggle();

        Assert.False(viewModel.IsOverlayOpen);
        Assert.False(controller.IsOpen);
        Assert.Equal(1, factory.LastWindow!.CloseCallCount);
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

        public int ShowCallCount { get; private set; }

        public int CloseCallCount { get; private set; }

        public void Show()
        {
            ShowCallCount++;
        }

        public void Close()
        {
            CloseCallCount++;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}