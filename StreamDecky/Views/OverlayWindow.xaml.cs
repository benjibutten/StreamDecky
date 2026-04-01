using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StreamDecky.Helpers;
using StreamDecky.ViewModels;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace StreamDecky.Views;

public partial class OverlayWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isDragging;
    private System.Windows.Point _dragStart;
    private double _startOffsetX;
    private double _startOffsetY;
    private IntPtr _previousForegroundWindow;

    public OverlayWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        // Save the foreground window (e.g. GTA V) BEFORE our overlay takes focus
        _previousForegroundWindow = OverlayInterop.GetCurrentForegroundWindow();
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        OverlayInterop.MakeTopmost(this);
        Focus();
        Activate();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseOverlay();
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseOverlay();
    }

    private void DeckButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is ButtonViewModel buttonVm)
        {
            if (buttonVm.HasAction)
            {
                // Save target window handle and close overlay
                var prevHwnd = _previousForegroundWindow;
                _viewModel.CloseOverlayCommand.Execute(null);
                Close();

                // Minimize the main StreamDecky window so it doesn't steal focus
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null)
                    mainWindow.WindowState = WindowState.Minimized;

                // Use aggressive focus restore with AttachThreadInput
                OverlayInterop.ForceSetForegroundWindow(prevHwnd);

                // Execute after a delay to let focus fully transfer
                _viewModel.ExecuteButtonCommand.Execute(buttonVm);
                return;
            }
        }
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(this);
        _startOffsetX = GridTranslate.X;
        _startOffsetY = GridTranslate.Y;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var pos = e.GetPosition(this);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        GridTranslate.X = _startOffsetX + dx;
        GridTranslate.Y = _startOffsetY + dy;
    }

    private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();

        // Save position back to viewmodel
        _viewModel.GridOffsetX = GridTranslate.X;
        _viewModel.GridOffsetY = GridTranslate.Y;
    }

    private void CloseOverlay()
    {
        _viewModel.CloseOverlayCommand.Execute(null);
        Close();
    }

    private void OverlayPrevPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PreviousPageCommand.Execute(null);
    }

    private void OverlayNextPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NextPageCommand.Execute(null);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_viewModel.IsOverlayOpen && IsVisible)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible)
                {
                    OverlayInterop.MakeTopmost(this);
                    Activate();
                    Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
