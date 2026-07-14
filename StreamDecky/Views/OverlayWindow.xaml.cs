using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
    private enum OverlayNavigationDirection
    {
        None,
        Up,
        Down,
        Left,
        Right
    }

    private const short LeftStickNavigationDeadZone = 16000;
    private static readonly TimeSpan InitialNavigationRepeatDelay = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan NavigationRepeatInterval = TimeSpan.FromMilliseconds(130);

    private readonly MainViewModel _viewModel;
    private readonly System.Windows.Threading.DispatcherTimer _gamepadTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };
    private bool _isDragging;
    private System.Windows.Point _dragStart;
    private double _startOffsetX;
    private double _startOffsetY;
    private IntPtr _previousForegroundWindow;
    private StickyNoteViewModel? _draggingStickyNote;
    private System.Windows.Point _stickyDragStart;
    private double _stickyStartX;
    private double _stickyStartY;
    private StickyNoteViewModel? _resizingStickyNote;
    private System.Windows.Point _stickyResizeStart;
    private double _stickyResizeStartX;
    private double _stickyResizeStartWidth;
    private double _stickyResizeStartHeight;
    private bool _isDraggingQuickTextPanel;
    private System.Windows.Point _quickTextPanelDragStart;
    private double _quickTextPanelStartX;
    private double _quickTextPanelStartY;
    private bool _isResizingQuickTextPanel;
    private System.Windows.Point _quickTextPanelResizeStart;
    private double _quickTextPanelResizeStartX;
    private double _quickTextPanelResizeStartWidth;
    private double _quickTextPanelResizeStartHeight;
    private bool _isDraggingMusicWidget;
    private System.Windows.Point _musicWidgetDragStart;
    private double _musicWidgetStartX;
    private double _musicWidgetStartY;
    private bool _isResizingMusicWidget;
    private System.Windows.Point _musicWidgetResizeStart;
    private double _musicWidgetResizeStartX;
    private double _musicWidgetResizeStartWidth;
    private double _musicWidgetResizeStartHeight;
    private bool _isDraggingMusicSeek;
    private bool _isUpdatingMusicSeekSlider;
    private readonly Dictionary<string, string> _quickTextSessionOverrides = new(StringComparer.Ordinal);
    private readonly HashSet<string> _quickTextEditingIds = new(StringComparer.Ordinal);
    public ObservableCollection<OverlayQuickTextSessionItemViewModel> OverlayQuickTextItems { get; } = new();
    private ushort _previousGamepadButtons;
    private OverlayNavigationDirection _heldNavigationDirection;
    private DateTime _nextNavigationRepeatAtUtc = DateTime.MinValue;

    public OverlayWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.MusicWidget.PropertyChanged += MusicWidget_PropertyChanged;
        RebuildOverlayQuickTextItems();
        _gamepadTimer.Tick += GamepadTimer_Tick;
    }

    /// <summary>
    /// Shows the overlay. The window instance is reused across open/close cycles,
    /// so all per-session state is reset here rather than in the constructor.
    /// </summary>
    public void ShowOverlay()
    {
        // Save the foreground window (e.g. GTA V) BEFORE our overlay takes focus
        _previousForegroundWindow = OverlayInterop.GetCurrentForegroundWindow();
        ResetQuickTextSessionState();
        Show();
        OverlayInterop.MakeTopmost(this);
        Activate();
        Focus();
        OverlayInterop.ForceFocus(this);
        EnsureOverlaySelection();
        Dispatcher.BeginInvoke(new Action(ClampQuickTextPanelToBounds), System.Windows.Threading.DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(ClampMusicWidgetToBounds), System.Windows.Threading.DispatcherPriority.Loaded);
        UpdateGamepadPolling();
    }

    /// <summary>
    /// Hides the overlay but keeps the window (and its visual tree) alive so the
    /// next hotkey press shows it instantly instead of rebuilding everything.
    /// </summary>
    public void HideOverlay()
    {
        _gamepadTimer.Stop();
        ResetGamepadNavigationState();
        Hide();
    }

    private void ResetQuickTextSessionState()
    {
        // Temporary quick-text edits are scoped to one overlay session; since the
        // window survives between sessions now, clear them on every show.
        _quickTextSessionOverrides.Clear();
        _quickTextEditingIds.Clear();
        RebuildOverlayQuickTextItems();
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
            ExecuteOverlayButton(buttonVm);
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
        HideOverlay();
    }

    private void OverlayPrevPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PreviousPageCommand.Execute(null);
        EnsureOverlaySelection();
    }

    private void OverlayNextPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NextPageCommand.Execute(null);
        EnsureOverlaySelection();
    }

    private void ExecuteOverlayButton(ButtonViewModel buttonVm)
    {
        if (!buttonVm.HasAction)
            return;

        if (buttonVm.ActionType == ActionType.LayoutNavigation)
        {
            _viewModel.FollowNavigationTargetCommand.Execute(buttonVm);
            EnsureOverlaySelection();
            return;
        }

        var prevHwnd = _previousForegroundWindow;
        _viewModel.CloseOverlayCommand.Execute(null);
        HideOverlay();

        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow != null)
            mainWindow.WindowState = WindowState.Minimized;

        OverlayInterop.ForceSetForegroundWindow(prevHwnd);
        _viewModel.ExecuteButtonCommand.Execute(buttonVm);
    }

    private void GamepadTimer_Tick(object? sender, EventArgs e)
    {
        if (!_viewModel.GamepadSupportEnabled)
        {
            ResetGamepadNavigationState();
            return;
        }

        if (!XInputInterop.TryGetFirstConnectedState(out var state))
        {
            ResetGamepadNavigationState();
            return;
        }

        EnsureOverlaySelection();

        ushort buttons = state.Gamepad.wButtons;

        if (IsNewButtonPress(buttons, XInputInterop.GamepadLeftShoulder))
        {
            _viewModel.PreviousPageCommand.Execute(null);
            EnsureOverlaySelection();
        }

        if (IsNewButtonPress(buttons, XInputInterop.GamepadRightShoulder))
        {
            _viewModel.NextPageCommand.Execute(null);
            EnsureOverlaySelection();
        }

        HandleDirectionalNavigation(buttons, state.Gamepad.sThumbLX, state.Gamepad.sThumbLY);

        if (IsNewButtonPress(buttons, XInputInterop.GamepadA) && _viewModel.SelectedButton != null)
            ExecuteOverlayButton(_viewModel.SelectedButton);

        _previousGamepadButtons = buttons;
    }

    private void HandleDirectionalNavigation(ushort buttons, short thumbLX, short thumbLY)
    {
        OverlayNavigationDirection direction = GetDirectionalInput(buttons, thumbLX, thumbLY);
        if (direction == OverlayNavigationDirection.None)
        {
            _heldNavigationDirection = OverlayNavigationDirection.None;
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (_heldNavigationDirection != direction)
        {
            MoveSelection(direction);
            _heldNavigationDirection = direction;
            _nextNavigationRepeatAtUtc = now + InitialNavigationRepeatDelay;
            return;
        }

        if (now >= _nextNavigationRepeatAtUtc)
        {
            MoveSelection(direction);
            _nextNavigationRepeatAtUtc = now + NavigationRepeatInterval;
        }
    }

    private OverlayNavigationDirection GetDirectionalInput(ushort buttons, short thumbLX, short thumbLY)
    {
        if (XInputInterop.IsButtonPressed(buttons, XInputInterop.GamepadDPadUp))
            return OverlayNavigationDirection.Up;
        if (XInputInterop.IsButtonPressed(buttons, XInputInterop.GamepadDPadDown))
            return OverlayNavigationDirection.Down;
        if (XInputInterop.IsButtonPressed(buttons, XInputInterop.GamepadDPadLeft))
            return OverlayNavigationDirection.Left;
        if (XInputInterop.IsButtonPressed(buttons, XInputInterop.GamepadDPadRight))
            return OverlayNavigationDirection.Right;

        if (Math.Abs(thumbLX) < LeftStickNavigationDeadZone && Math.Abs(thumbLY) < LeftStickNavigationDeadZone)
            return OverlayNavigationDirection.None;

        if (Math.Abs(thumbLX) > Math.Abs(thumbLY))
            return thumbLX > 0 ? OverlayNavigationDirection.Right : OverlayNavigationDirection.Left;

        return thumbLY > 0 ? OverlayNavigationDirection.Up : OverlayNavigationDirection.Down;
    }

    private void MoveSelection(OverlayNavigationDirection direction)
    {
        var configuredButtons = _viewModel.Buttons.Where(static b => b.IsConfigured).ToList();
        if (configuredButtons.Count == 0)
            return;

        var current = _viewModel.SelectedButton;
        if (current == null || !current.IsConfigured)
        {
            _viewModel.SelectButtonCommand.Execute(configuredButtons[0]);
            return;
        }

        int columns = Math.Max(1, _viewModel.Columns);
        int rows = Math.Max(1, _viewModel.Rows);
        int currentRow = current.Index / columns;
        int currentCol = current.Index % columns;

        ButtonViewModel? bestCandidate = null;
        int bestScore = int.MaxValue;

        foreach (var candidate in configuredButtons)
        {
            if (ReferenceEquals(candidate, current))
                continue;

            int candidateRow = candidate.Index / columns;
            int candidateCol = candidate.Index % columns;

            int rowDelta = candidateRow - currentRow;
            int colDelta = candidateCol - currentCol;

            if (!IsCandidateInDirection(direction, rowDelta, colDelta))
                continue;

            int primaryDistance = direction is OverlayNavigationDirection.Up or OverlayNavigationDirection.Down
                ? Math.Abs(rowDelta)
                : Math.Abs(colDelta);
            int secondaryDistance = direction is OverlayNavigationDirection.Up or OverlayNavigationDirection.Down
                ? Math.Abs(colDelta)
                : Math.Abs(rowDelta);

            int score = (primaryDistance * 100) + secondaryDistance;
            if (score < bestScore)
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        bestCandidate ??= FindWrapCandidate(configuredButtons, current, direction, columns, rows);
        if (bestCandidate != null)
            _viewModel.SelectButtonCommand.Execute(bestCandidate);
    }

    private static bool IsCandidateInDirection(OverlayNavigationDirection direction, int rowDelta, int colDelta)
    {
        return direction switch
        {
            OverlayNavigationDirection.Up => rowDelta < 0,
            OverlayNavigationDirection.Down => rowDelta > 0,
            OverlayNavigationDirection.Left => colDelta < 0,
            OverlayNavigationDirection.Right => colDelta > 0,
            _ => false
        };
    }

    private static ButtonViewModel? FindWrapCandidate(
        IReadOnlyList<ButtonViewModel> candidates,
        ButtonViewModel current,
        OverlayNavigationDirection direction,
        int columns,
        int rows)
    {
        int currentRow = current.Index / columns;
        int currentCol = current.Index % columns;

        ButtonViewModel? bestCandidate = null;
        int bestScore = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (ReferenceEquals(candidate, current))
                continue;

            int candidateRow = candidate.Index / columns;
            int candidateCol = candidate.Index % columns;

            int score = direction switch
            {
                OverlayNavigationDirection.Right => (candidateCol * 100) + Math.Abs(candidateRow - currentRow),
                OverlayNavigationDirection.Left => ((columns - 1 - candidateCol) * 100) + Math.Abs(candidateRow - currentRow),
                OverlayNavigationDirection.Down => (candidateRow * 100) + Math.Abs(candidateCol - currentCol),
                OverlayNavigationDirection.Up => ((rows - 1 - candidateRow) * 100) + Math.Abs(candidateCol - currentCol),
                _ => int.MaxValue
            };

            if (score < bestScore)
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private void EnsureOverlaySelection()
    {
        var configuredButtons = _viewModel.Buttons.Where(static b => b.IsConfigured).ToList();
        if (configuredButtons.Count == 0)
        {
            _viewModel.SelectButtonCommand.Execute(null);
            return;
        }

        var selected = _viewModel.SelectedButton;
        if (selected == null || !selected.IsConfigured || !_viewModel.Buttons.Contains(selected))
            _viewModel.SelectButtonCommand.Execute(configuredButtons[0]);
    }

    private bool IsNewButtonPress(ushort currentButtons, ushort button)
    {
        return XInputInterop.IsButtonPressed(currentButtons, button)
            && !XInputInterop.IsButtonPressed(_previousGamepadButtons, button);
    }

    private void ResetGamepadNavigationState()
    {
        _previousGamepadButtons = 0;
        _heldNavigationDirection = OverlayNavigationDirection.None;
        _nextNavigationRepeatAtUtc = DateTime.MinValue;
    }

    private void UpdateGamepadPolling()
    {
        if (_viewModel.GamepadSupportEnabled)
        {
            if (!_gamepadTimer.IsEnabled)
                _gamepadTimer.Start();

            return;
        }

        _gamepadTimer.Stop();
        ResetGamepadNavigationState();
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

    private void StickyNoteResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is StickyNoteViewModel note)
        {
            _resizingStickyNote = note;
            _stickyResizeStart = e.GetPosition(this);
            _stickyResizeStartX = note.X;
            _stickyResizeStartWidth = note.Width;
            _stickyResizeStartHeight = note.Height;
            element.CaptureMouse();
            e.Handled = true;
        }
    }

    private void StickyNoteResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_resizingStickyNote == null)
            return;

        if (sender is not UIElement element || !element.IsMouseCaptured)
            return;

        var pos = e.GetPosition(this);
        var dx = pos.X - _stickyResizeStart.X;
        var dy = pos.Y - _stickyResizeStart.Y;

        double rightEdge = _stickyResizeStartX + _stickyResizeStartWidth;
        double desiredWidth = _stickyResizeStartWidth - dx;
        double maxWidthByRightEdge = Math.Max(StickyNoteViewModel.MinWidth, Math.Min(StickyNoteViewModel.MaxWidth, rightEdge));
        double clampedWidth = Math.Clamp(desiredWidth, StickyNoteViewModel.MinWidth, maxWidthByRightEdge);

        _resizingStickyNote.Width = clampedWidth;
        _resizingStickyNote.Height = _stickyResizeStartHeight + dy;

        double desiredX = rightEdge - _resizingStickyNote.Width;
        double maxX = Math.Max(0, ActualWidth - _resizingStickyNote.Width - 12);
        double maxY = Math.Max(0, ActualHeight - _resizingStickyNote.DisplayHeight - 12);

        _resizingStickyNote.X = Math.Clamp(desiredX, 0, maxX);
        _resizingStickyNote.Y = Math.Clamp(_resizingStickyNote.Y, 0, maxY);

        e.Handled = true;
    }

    private void StickyNoteResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();

        _resizingStickyNote = null;
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

    private void QuickTextPanelHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingQuickTextPanel = true;
        _quickTextPanelDragStart = e.GetPosition(this);
        _quickTextPanelStartX = _viewModel.QuickTextPanelX;
        _quickTextPanelStartY = _viewModel.QuickTextPanelY;

        if (sender is UIElement element)
            element.CaptureMouse();

        e.Handled = true;
    }

    private void QuickTextPanelHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingQuickTextPanel)
            return;

        if (sender is not UIElement element || !element.IsMouseCaptured)
            return;

        var pos = e.GetPosition(this);
        double dx = pos.X - _quickTextPanelDragStart.X;
        double dy = pos.Y - _quickTextPanelDragStart.Y;

        UpdateQuickTextPanelPosition(_quickTextPanelStartX + dx, _quickTextPanelStartY + dy);
        e.Handled = true;
    }

    private void QuickTextPanelHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();

        _isDraggingQuickTextPanel = false;
    }

    private void QuickTextCopy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverlayQuickTextSessionItemViewModel item })
            return;

        if (string.IsNullOrWhiteSpace(item.SessionText))
            return;

        try
        {
            System.Windows.Clipboard.SetText(item.SessionText);
        }
        catch
        {
            // Clipboard can fail if another process has it locked.
        }
    }

    private void QuickTextToggleEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverlayQuickTextSessionItemViewModel item })
            return;

        item.IsEditing = !item.IsEditing;
        if (item.IsEditing)
            FocusQuickTextInlineEditor(item);

        e.Handled = true;
    }

    private void QuickTextInlineEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        if (sender is FrameworkElement { DataContext: OverlayQuickTextSessionItemViewModel item })
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }

    private void UpdateQuickTextPanelPosition(double desiredX, double desiredY)
    {
        double panelWidth = QuickTextPanel.ActualWidth > 1 ? QuickTextPanel.ActualWidth : _viewModel.QuickTextPanelWidth;
        double panelHeight = QuickTextPanel.ActualHeight > 1 ? QuickTextPanel.ActualHeight : _viewModel.QuickTextPanelHeight;

        double maxX = Math.Max(0, ActualWidth - panelWidth - 12);
        double maxY = Math.Max(0, ActualHeight - panelHeight - 12);
        double minY = Math.Min(42, maxY);

        _viewModel.QuickTextPanelX = Math.Clamp(desiredX, 0, maxX);
        _viewModel.QuickTextPanelY = Math.Clamp(desiredY, minY, maxY);
    }

    private void ClampQuickTextPanelToBounds()
    {
        UpdateQuickTextPanelPosition(_viewModel.QuickTextPanelX, _viewModel.QuickTextPanelY);
    }

    private void QuickTextPanelResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isResizingQuickTextPanel = true;
        _quickTextPanelResizeStart = e.GetPosition(this);
        _quickTextPanelResizeStartX = _viewModel.QuickTextPanelX;
        _quickTextPanelResizeStartWidth = _viewModel.QuickTextPanelWidth;
        _quickTextPanelResizeStartHeight = _viewModel.QuickTextPanelHeight;

        if (sender is UIElement element)
            element.CaptureMouse();

        e.Handled = true;
    }

    private void QuickTextPanelResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizingQuickTextPanel)
            return;

        if (sender is not UIElement element || !element.IsMouseCaptured)
            return;

        var pos = e.GetPosition(this);
        double dx = pos.X - _quickTextPanelResizeStart.X;
        double dy = pos.Y - _quickTextPanelResizeStart.Y;

        // Match sticky-note resize behavior: bottom-left handle moves the left edge while keeping the right edge anchored.
        double rightEdge = Math.Min(_quickTextPanelResizeStartX + _quickTextPanelResizeStartWidth, ActualWidth - 12);
        double desiredWidth = _quickTextPanelResizeStartWidth - dx;
        double maxWidthByRightEdge = Math.Max(
            Models.DeckProfile.MinQuickTextPanelWidth,
            Math.Min(Models.DeckProfile.MaxQuickTextPanelWidth, rightEdge));
        double clampedWidth = Math.Clamp(desiredWidth, Models.DeckProfile.MinQuickTextPanelWidth, maxWidthByRightEdge);

        double desiredHeight = _quickTextPanelResizeStartHeight + dy;
        double maxHeightByBounds = Math.Min(
            Models.DeckProfile.MaxQuickTextPanelHeight,
            Math.Max(Models.DeckProfile.MinQuickTextPanelHeight, ActualHeight - _viewModel.QuickTextPanelY - 12));
        double clampedHeight = Math.Clamp(desiredHeight, Models.DeckProfile.MinQuickTextPanelHeight, maxHeightByBounds);

        double desiredX = rightEdge - clampedWidth;
        double maxX = Math.Max(0, ActualWidth - clampedWidth - 12);
        _viewModel.QuickTextPanelX = Math.Clamp(desiredX, 0, maxX);
        _viewModel.QuickTextPanelWidth = clampedWidth;
        _viewModel.QuickTextPanelHeight = clampedHeight;
        ClampQuickTextPanelToBounds();

        e.Handled = true;
    }

    private void QuickTextPanelResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();

        _isResizingQuickTextPanel = false;
    }

    private void MusicWidgetHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingMusicWidget = true;
        _musicWidgetDragStart = e.GetPosition(this);
        _musicWidgetStartX = _viewModel.MusicWidgetX;
        _musicWidgetStartY = _viewModel.MusicWidgetY;

        if (sender is UIElement element)
            element.CaptureMouse();

        e.Handled = true;
    }

    private void MusicWidgetHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingMusicWidget)
            return;

        if (sender is not UIElement element || !element.IsMouseCaptured)
            return;

        var pos = e.GetPosition(this);
        double dx = pos.X - _musicWidgetDragStart.X;
        double dy = pos.Y - _musicWidgetDragStart.Y;

        UpdateMusicWidgetPosition(_musicWidgetStartX + dx, _musicWidgetStartY + dy);
        e.Handled = true;
    }

    private void MusicWidgetHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();

        _isDraggingMusicWidget = false;
    }

    private void UpdateMusicWidgetPosition(double desiredX, double desiredY)
    {
        double panelWidth = MusicWidgetPanel.ActualWidth > 1 ? MusicWidgetPanel.ActualWidth : _viewModel.MusicWidgetWidth;
        double panelHeight = MusicWidgetPanel.ActualHeight > 1 ? MusicWidgetPanel.ActualHeight : _viewModel.MusicWidgetHeight;

        double maxX = Math.Max(0, ActualWidth - panelWidth - 12);
        double maxY = Math.Max(0, ActualHeight - panelHeight - 12);
        double minY = Math.Min(42, maxY);

        _viewModel.MusicWidgetX = Math.Clamp(desiredX, 0, maxX);
        _viewModel.MusicWidgetY = Math.Clamp(desiredY, minY, maxY);
    }

    private void ClampMusicWidgetToBounds()
    {
        UpdateMusicWidgetPosition(_viewModel.MusicWidgetX, _viewModel.MusicWidgetY);
    }

    private void MusicWidgetResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isResizingMusicWidget = true;
        _musicWidgetResizeStart = e.GetPosition(this);
        _musicWidgetResizeStartX = _viewModel.MusicWidgetX;
        _musicWidgetResizeStartWidth = _viewModel.MusicWidgetWidth;
        _musicWidgetResizeStartHeight = _viewModel.MusicWidgetHeight;

        if (sender is UIElement element)
            element.CaptureMouse();

        e.Handled = true;
    }

    private void MusicWidgetResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizingMusicWidget)
            return;

        if (sender is not UIElement element || !element.IsMouseCaptured)
            return;

        var pos = e.GetPosition(this);
        double dx = pos.X - _musicWidgetResizeStart.X;
        double dy = pos.Y - _musicWidgetResizeStart.Y;

        // Bottom-left handle: the left edge follows the mouse while the right edge stays anchored.
        double rightEdge = Math.Min(_musicWidgetResizeStartX + _musicWidgetResizeStartWidth, ActualWidth - 12);
        double desiredWidth = _musicWidgetResizeStartWidth - dx;
        double maxWidthByRightEdge = Math.Max(
            Models.DeckProfile.MinMusicWidgetWidth,
            Math.Min(Models.DeckProfile.MaxMusicWidgetWidth, rightEdge));
        double clampedWidth = Math.Clamp(desiredWidth, Models.DeckProfile.MinMusicWidgetWidth, maxWidthByRightEdge);

        double desiredHeight = _musicWidgetResizeStartHeight + dy;
        double maxHeightByBounds = Math.Min(
            Models.DeckProfile.MaxMusicWidgetHeight,
            Math.Max(Models.DeckProfile.MinMusicWidgetHeight, ActualHeight - _viewModel.MusicWidgetY - 12));
        double clampedHeight = Math.Clamp(desiredHeight, Models.DeckProfile.MinMusicWidgetHeight, maxHeightByBounds);

        double desiredX = rightEdge - clampedWidth;
        double maxX = Math.Max(0, ActualWidth - clampedWidth - 12);
        _viewModel.MusicWidgetX = Math.Clamp(desiredX, 0, maxX);
        _viewModel.MusicWidgetWidth = clampedWidth;
        _viewModel.MusicWidgetHeight = clampedHeight;
        ClampMusicWidgetToBounds();

        e.Handled = true;
    }

    private void MusicWidgetResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();

        _isResizingMusicWidget = false;
    }

    private void MusicWidgetResizeRightHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isResizingMusicWidget = true;
        _musicWidgetResizeStart = e.GetPosition(this);
        _musicWidgetResizeStartX = _viewModel.MusicWidgetX;
        _musicWidgetResizeStartWidth = _viewModel.MusicWidgetWidth;
        _musicWidgetResizeStartHeight = _viewModel.MusicWidgetHeight;

        if (sender is UIElement element)
            element.CaptureMouse();

        e.Handled = true;
    }

    private void MusicWidgetResizeRightHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizingMusicWidget)
            return;

        if (sender is not UIElement element || !element.IsMouseCaptured)
            return;

        var pos = e.GetPosition(this);
        double dx = pos.X - _musicWidgetResizeStart.X;
        double dy = pos.Y - _musicWidgetResizeStart.Y;

        // Bottom-right handle: the left edge stays anchored while width and height follow the mouse.
        double maxWidthByBounds = Math.Min(
            Models.DeckProfile.MaxMusicWidgetWidth,
            Math.Max(Models.DeckProfile.MinMusicWidgetWidth, ActualWidth - _musicWidgetResizeStartX - 12));
        double clampedWidth = Math.Clamp(
            _musicWidgetResizeStartWidth + dx,
            Models.DeckProfile.MinMusicWidgetWidth,
            maxWidthByBounds);

        double maxHeightByBounds = Math.Min(
            Models.DeckProfile.MaxMusicWidgetHeight,
            Math.Max(Models.DeckProfile.MinMusicWidgetHeight, ActualHeight - _viewModel.MusicWidgetY - 12));
        double clampedHeight = Math.Clamp(
            _musicWidgetResizeStartHeight + dy,
            Models.DeckProfile.MinMusicWidgetHeight,
            maxHeightByBounds);

        _viewModel.MusicWidgetWidth = clampedWidth;
        _viewModel.MusicWidgetHeight = clampedHeight;
        ClampMusicWidgetToBounds();

        e.Handled = true;
    }

    private void MusicWidgetResizeRightHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();

        _isResizingMusicWidget = false;
    }

    private void MusicWidget_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MusicWidgetViewModel.PositionSeconds)
            or nameof(MusicWidgetViewModel.DurationSeconds)))
        {
            return;
        }

        if (_isDraggingMusicSeek)
            return;

        var widget = _viewModel.MusicWidget;
        _isUpdatingMusicSeekSlider = true;
        try
        {
            MusicSeekSlider.Maximum = Math.Max(1, widget.DurationSeconds);
            MusicSeekSlider.Value = Math.Clamp(widget.PositionSeconds, 0, MusicSeekSlider.Maximum);
            MusicSeekSlider.IsEnabled = widget.DurationSeconds > 0;
        }
        finally
        {
            _isUpdatingMusicSeekSlider = false;
        }
    }

    private void MusicSeek_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _isDraggingMusicSeek = true;
        _viewModel.MusicWidget.IsSeekDragging = true;
    }

    private void MusicSeek_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isDraggingMusicSeek = false;
        _viewModel.MusicWidget.IsSeekDragging = false;
        _ = _viewModel.MusicWidget.SeekToAsync(MusicSeekSlider.Value);
    }

    private void MusicSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Only direct clicks on the track (IsMoveToPointEnabled) should seek here;
        // programmatic updates and thumb drags are handled elsewhere.
        if (_isUpdatingMusicSeekSlider || _isDraggingMusicSeek)
            return;

        _ = _viewModel.MusicWidget.SeekToAsync(e.NewValue);
    }

    private void MusicDelayOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
            _viewModel.MusicWidget.SetDelayedStartSecondsCommand.Execute(tag);

        MusicDelayMenuToggle.IsChecked = false;
    }

    private void MusicTrackRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MusicTrackItemViewModel track })
            return;

        if (e.ClickCount >= 2)
        {
            _viewModel.MusicWidget.PlayTrackCommand.Execute(track);
            e.Handled = true;
            return;
        }

        _viewModel.MusicWidget.ToggleTrackSelectionCommand.Execute(track);
    }

    private void QuickTextAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverlayQuickTextSessionItemViewModel item })
            return;

        string itemText = item.SessionText;
        if (string.IsNullOrWhiteSpace(itemText))
            return;

        var prevHwnd = _previousForegroundWindow;
        _viewModel.CloseOverlayCommand.Execute(null);
        HideOverlay();

        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow != null)
            mainWindow.WindowState = WindowState.Minimized;

        OverlayInterop.ForceSetForegroundWindow(prevHwnd);
        _viewModel.ExecuteQuickTextAction(itemText);
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

    private void FocusQuickTextInlineEditor(OverlayQuickTextSessionItemViewModel item)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var editor = FindDescendant(this,
                static tb => tb.Tag is string tag && tag == "QuickTextInlineEditor",
                item);

            if (editor == null)
                return;

            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void RebuildOverlayQuickTextItems()
    {
        foreach (var existing in OverlayQuickTextItems)
            existing.PropertyChanged -= OverlayQuickTextItem_PropertyChanged;

        OverlayQuickTextItems.Clear();

        foreach (var sourceItem in _viewModel.QuickTextItems)
        {
            string sessionText = _quickTextSessionOverrides.TryGetValue(sourceItem.Id, out var overrideText)
                ? overrideText
                : sourceItem.Text;

            var sessionItem = new OverlayQuickTextSessionItemViewModel(sourceItem.Id, sessionText)
            {
                IsEditing = _quickTextEditingIds.Contains(sourceItem.Id)
            };

            sessionItem.PropertyChanged += OverlayQuickTextItem_PropertyChanged;
            OverlayQuickTextItems.Add(sessionItem);
        }
    }

    private void OverlayQuickTextItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OverlayQuickTextSessionItemViewModel sessionItem)
            return;

        if (e.PropertyName == nameof(OverlayQuickTextSessionItemViewModel.SessionText))
        {
            _quickTextSessionOverrides[sessionItem.Id] = sessionItem.SessionText;
        }
        else if (e.PropertyName == nameof(OverlayQuickTextSessionItemViewModel.IsEditing))
        {
            if (sessionItem.IsEditing)
                _quickTextEditingIds.Add(sessionItem.Id);
            else
                _quickTextEditingIds.Remove(sessionItem.Id);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.QuickTextItems))
            RebuildOverlayQuickTextItems();

        if (e.PropertyName is nameof(MainViewModel.QuickTextItems)
            or nameof(MainViewModel.QuickTextPanelWidth)
            or nameof(MainViewModel.QuickTextPanelHeight)
            or nameof(MainViewModel.QuickTextFontSize))
        {
            Dispatcher.BeginInvoke(new Action(ClampQuickTextPanelToBounds), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        if (e.PropertyName is nameof(MainViewModel.MusicWidgetVisible)
            or nameof(MainViewModel.MusicWidgetMinimized)
            or nameof(MainViewModel.MusicWidgetWidth)
            or nameof(MainViewModel.MusicWidgetHeight))
        {
            Dispatcher.BeginInvoke(new Action(ClampMusicWidgetToBounds), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static System.Windows.Controls.TextBox? FindDescendant(
        DependencyObject root,
        Func<System.Windows.Controls.TextBox, bool> predicate,
        object dataContext)
    {
        int childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is System.Windows.Controls.TextBox textBox
                && ReferenceEquals(textBox.DataContext, dataContext)
                && predicate(textBox))
            {
                return textBox;
            }

            var nested = FindDescendant(child, predicate, dataContext);
            if (nested != null)
                return nested;
        }

        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _gamepadTimer.Stop();
        _gamepadTimer.Tick -= GamepadTimer_Tick;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.MusicWidget.PropertyChanged -= MusicWidget_PropertyChanged;

        foreach (var item in OverlayQuickTextItems)
            item.PropertyChanged -= OverlayQuickTextItem_PropertyChanged;

        base.OnClosed(e);
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
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
