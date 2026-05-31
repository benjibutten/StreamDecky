using StreamDecky.ViewModels;
using StreamDecky.Views;

namespace StreamDecky.Services;

public interface IOverlayWindowHandle
{
    event EventHandler? Closed;

    void Show();

    void Close();
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

    public bool IsOpen => _window != null;

    public void Toggle()
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }

        Open();
    }

    public void Open()
    {
        if (_window != null)
            return;

        _viewModel.OpenOverlayCommand.Execute(null);
        _window = _factory.Create(_viewModel);
        _window.Closed += OnWindowClosed;
        _window.Show();
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

        public void Show()
        {
            _window.Show();
        }

        public void Close()
        {
            _window.Close();
        }
    }
}