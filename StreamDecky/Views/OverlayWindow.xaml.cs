using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StreamDecky.Helpers;
using StreamDecky.Models;
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
    private StickyNoteViewModel? _draggingStickyNote;
    private System.Windows.Point _stickyDragStart;
    private double _stickyStartX;
    private double _stickyStartY;

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
                if (buttonVm.ActionType == ActionType.LayoutNavigation)
                {
                    _viewModel.FollowNavigationTargetCommand.Execute(buttonVm);
                    return;
                }

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

    private void StickyNoteHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is StickyNoteViewModel note)
        {
            if (e.ClickCount >= 2)
            {
                StartInlineTitleEdit(note);
                e.Handled = true;
                return;
            }

            _draggingStickyNote = note;
            _stickyDragStart = e.GetPosition(this);
            _stickyStartX = note.X;
            _stickyStartY = note.Y;
            element.CaptureMouse();
            e.Handled = true;
        }
    }

    private void StickyNoteText_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is StickyNoteViewModel note)
        {
            StartInlineTitleEdit(note);
            e.Handled = true;
        }
    }

    private void StickyNoteTitleEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox editor
            || editor.DataContext is not StickyNoteViewModel note)
        {
            return;
        }

        editor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        note.IsEditingTitle = false;
    }

    private void StickyNoteTitleEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox editor
            || editor.DataContext is not StickyNoteViewModel note)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            editor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            note.IsEditingTitle = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Revert to the currently stored title.
            editor.Text = note.Title;
            note.IsEditingTitle = false;
            e.Handled = true;
        }
    }

    private void StickyNoteHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingStickyNote == null)
            return;

        if (sender is not UIElement element || !element.IsMouseCaptured)
            return;

        var pos = e.GetPosition(this);
        var dx = pos.X - _stickyDragStart.X;
        var dy = pos.Y - _stickyDragStart.Y;

        double maxX = Math.Max(0, ActualWidth - _draggingStickyNote.Width - 12);
        double maxY = Math.Max(0, ActualHeight - _draggingStickyNote.DisplayHeight - 12);

        _draggingStickyNote.X = Math.Clamp(_stickyStartX + dx, 0, maxX);
        _draggingStickyNote.Y = Math.Clamp(_stickyStartY + dy, 0, maxY);
    }

    private void StickyNoteHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();

        _draggingStickyNote = null;
    }

    private void StickyNoteRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is StickyNoteViewModel note)
            _viewModel.RemoveStickyNoteCommand.Execute(note);
    }

    private void StickyNoteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.DataContext is StickyNoteViewModel note
            && element.Tag is string color)
        {
            _viewModel.SetStickyNoteColor(note, color);
        }
    }

    private void StickyNoteMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is StickyNoteViewModel note)
        {
            note.IsMinimized = !note.IsMinimized;

            double maxX = Math.Max(0, ActualWidth - note.Width - 12);
            double maxY = Math.Max(0, ActualHeight - note.DisplayHeight - 12);
            note.X = Math.Clamp(note.X, 0, maxX);
            note.Y = Math.Clamp(note.Y, 0, maxY);
        }
    }

    private void StartInlineTitleEdit(StickyNoteViewModel note)
    {
        note.IsEditingTitle = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var editor = FindDescendant(this,
                static tb => tb.Tag is string tag && tag == "StickyNoteTitleEditor",
                note);

            if (editor == null)
                return;

            editor.Focus();
            editor.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static System.Windows.Controls.TextBox? FindDescendant(
        DependencyObject root,
        Func<System.Windows.Controls.TextBox, bool> predicate,
        StickyNoteViewModel note)
    {
        int childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is System.Windows.Controls.TextBox textBox
                && ReferenceEquals(textBox.DataContext, note)
                && predicate(textBox))
            {
                return textBox;
            }

            var nested = FindDescendant(child, predicate, note);
            if (nested != null)
                return nested;
        }

        return null;
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
