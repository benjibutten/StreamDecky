using StreamDecky.ViewModels;
using StreamDecky.Views;

namespace StreamDecky.Services;

public interface IOverlayWindowHandle
{
    event EventHandler? Closed;

    bool IsOverlayVisible { get; }

    void ShowOverlay();

    void HideOverlay();
}

public interface IOverlayWindowFactory
{
    IOverlayWindowHandle Create(MainViewModel viewModel);
}

public sealed class OverlayWindowController
{
    private readonly MainViewModel _viewModel;
    private readonly IOverlayWindowFactory _factory;
    private IOverlayWindowHandle? _window;

    public OverlayWindowController(MainViewModel viewModel, IOverlayWindowFactory? factory = null)
    {
        _viewModel = viewModel;
        _factory = factory ?? new OverlayWindowFactory();
    }

    public bool IsOpen => _window?.IsOverlayVisible == true;

    public void Toggle()
    {
        if (IsOpen)
        {
            _window!.HideOverlay();
            _viewModel.IsOverlayOpen = false;
            return;
        }

        Open();
    }

    public void Open()
    {
        if (IsOpen)
            return;

        // The window is kept alive across open/close cycles so the hotkey only
        // pays the full visual-tree construction cost once.
        if (_window == null)
        {
            _window = _factory.Create(_viewModel);
            _window.Closed += OnWindowClosed;
        }

        _viewModel.OpenOverlayCommand.Execute(null);
        _window.ShowOverlay();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window != null)
            _window.Closed -= OnWindowClosed;

        _window = null;
        _viewModel.IsOverlayOpen = false;
    }

    private sealed class OverlayWindowFactory : IOverlayWindowFactory
    {
        public IOverlayWindowHandle Create(MainViewModel viewModel)
        {
            return new OverlayWindowAdapter(new OverlayWindow(viewModel));
        }
    }

    private sealed class OverlayWindowAdapter : IOverlayWindowHandle
    {
        private readonly OverlayWindow _window;

        public OverlayWindowAdapter(OverlayWindow window)
        {
            _window = window;
        }

        public event EventHandler? Closed
        {
            add => _window.Closed += value;
            remove => _window.Closed -= value;
        }

        public bool IsOverlayVisible => _window.IsVisible;

        public void ShowOverlay()
        {
            _window.ShowOverlay();
        }

        public void HideOverlay()
        {
            _window.HideOverlay();
        }
    }
}
