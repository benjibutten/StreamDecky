using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using StreamDecky.Helpers;
using StreamDecky.ViewModels;
using StreamDecky.Views;

namespace StreamDecky;

using Popup = System.Windows.Controls.Primitives.Popup;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private const int HOTKEY_ID = 9000;
    private static readonly TimeSpan GamepadToggleCooldown = TimeSpan.FromMilliseconds(350);
    private HwndSource? _hwndSource;
    private OverlayWindow? _overlayWindow;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private readonly System.Windows.Threading.DispatcherTimer _gamepadToggleTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(50)
    };
    private ushort _previousGamepadButtons;
    private DateTime _nextGamepadToggleAllowedAtUtc = DateTime.MinValue;

    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "StreamDecky";

    public MainWindow()
    {
        DataContext = _viewModel;
        InitializeComponent();
        InitializeTrayIcon();
        SyncStartWithWindows();

        _gamepadToggleTimer.Tick += GamepadToggleTimer_Tick;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateGamepadTogglePolling();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "StreamDecky",
            Visible = true
        };

        _trayIconImage = TryLoadTrayIcon();
        _trayIcon.Icon = _trayIconImage ?? System.Drawing.SystemIcons.Application;

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private static System.Drawing.Icon? TryLoadTrayIcon()
    {
        try
        {
            // Executable icon works in dev and single-file publish without extra content files.
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && System.IO.File.Exists(exePath))
                return System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch
        {
            // Use system icon fallback in caller.
        }

        return null;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        DisposeGamepadTogglePolling();
        DisposeTrayIcon();
        OverlayInterop.UnregisterGlobalHotkey(this, HOTKEY_ID);
        _hwndSource?.RemoveHook(WndProc);
        System.Windows.Application.Current.Shutdown();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.GamepadSupportEnabled))
        {
            UpdateGamepadTogglePolling();
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.HotkeyModifiers) or nameof(MainViewModel.HotkeyVk))
        {
            if (_hwndSource != null)
            {
                OverlayInterop.UnregisterGlobalHotkey(this, HOTKEY_ID);
                OverlayInterop.RegisterGlobalHotkey(this, HOTKEY_ID,
                    _viewModel.HotkeyModifiers, _viewModel.HotkeyVk);
            }
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.StartWithWindows))
            SyncStartWithWindows();
    }

    private void UpdateGamepadTogglePolling()
    {
        if (_viewModel.GamepadSupportEnabled)
        {
            if (!_gamepadToggleTimer.IsEnabled)
                _gamepadToggleTimer.Start();
            return;
        }

        _gamepadToggleTimer.Stop();
        _previousGamepadButtons = 0;
    }

    private void GamepadToggleTimer_Tick(object? sender, EventArgs e)
    {
        if (!_viewModel.GamepadSupportEnabled)
            return;

        if (!XInputInterop.TryGetFirstConnectedState(out var state))
        {
            _previousGamepadButtons = 0;
            return;
        }

        ushort toggleButtons = _viewModel.GamepadToggleButtons;
        if (toggleButtons == 0)
            return;

        ushort buttons = state.Gamepad.wButtons;
        bool comboPressed = XInputInterop.AreButtonsPressed(buttons, toggleButtons);
        bool previousComboPressed = XInputInterop.AreButtonsPressed(_previousGamepadButtons, toggleButtons);

        if (comboPressed && !previousComboPressed)
        {
            DateTime now = DateTime.UtcNow;
            if (now >= _nextGamepadToggleAllowedAtUtc)
            {
                _nextGamepadToggleAllowedAtUtc = now + GamepadToggleCooldown;
                ToggleOverlay();
            }
        }

        _previousGamepadButtons = buttons;
    }

    private void DisposeGamepadTogglePolling()
    {
        _gamepadToggleTimer.Stop();
        _gamepadToggleTimer.Tick -= GamepadToggleTimer_Tick;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _previousGamepadButtons = 0;
    }

    private void DisposeTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }

    private void SyncStartWithWindows()
    {
        var exePath = Environment.ProcessPath;
        if (exePath == null) return;

        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
        if (key == null) return;

        if (_viewModel.StartWithWindows)
            key.SetValue(AppRegistryName, $"\"{exePath}\"");
        else
            key.DeleteValue(AppRegistryName, false);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        OverlayInterop.RegisterGlobalHotkey(this, HOTKEY_ID,
            _viewModel.HotkeyModifiers, _viewModel.HotkeyVk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleOverlay();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleOverlay()
    {
        if (_viewModel.IsOverlayOpen && _overlayWindow != null)
        {
            _overlayWindow.Close();
        }
        else
        {
            OpenOverlay();
        }
    }

    private void OpenOverlay()
    {
        _viewModel.OpenOverlayCommand.Execute(null);
        _overlayWindow = new OverlayWindow(_viewModel);
        _overlayWindow.Closed += (_, _) =>
        {
            _viewModel.IsOverlayOpen = false;
            _overlayWindow = null;
        };
        _overlayWindow.Show();
    }

    private void OpenOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsOverlayOpen && _overlayWindow != null)
        {
            _overlayWindow.Close();
            return;
        }
        OpenOverlay();
    }

    private void ToggleNotesAreasPopup_Click(object sender, RoutedEventArgs e)
    {
        var popup = GetNotesAreasPopup();
        if (popup != null)
            popup.IsOpen = !popup.IsOpen;
    }

    private void SetTextInput_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NewButtonCommand.Execute(null);
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DuplicateButtonCommand.Execute(null);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearButtonCommand.Execute(null);
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedButton == null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Button Image",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All Files|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            _viewModel.SelectedButton.ImagePath = dlg.FileName;
        }
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedButton != null)
        {
            _viewModel.SelectedButton.ImagePath = string.Empty;
        }
    }

    private void BtnBgColorPicker_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedButton == null) return;
        var color = ShowColorDialog(_viewModel.SelectedButton.BackgroundColor);
        if (color != null)
            _viewModel.SelectedButton.BackgroundColor = color;
    }

    private void BtnTextColorPicker_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedButton == null) return;
        var color = ShowColorDialog(_viewModel.SelectedButton.TextColor);
        if (color != null)
            _viewModel.SelectedButton.TextColor = color;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var oldMod = _viewModel.HotkeyModifiers;
        var oldVk = _viewModel.HotkeyVk;
        var oldStartup = _viewModel.StartWithWindows;

        var settingsWindow = new SettingsWindow(_viewModel);
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();

        // Re-register hotkey if changed
        if (oldMod != _viewModel.HotkeyModifiers || oldVk != _viewModel.HotkeyVk)
        {
            OverlayInterop.UnregisterGlobalHotkey(this, HOTKEY_ID);
            OverlayInterop.RegisterGlobalHotkey(this, HOTKEY_ID,
                _viewModel.HotkeyModifiers, _viewModel.HotkeyVk);
        }

        // Sync startup setting if changed
        if (oldStartup != _viewModel.StartWithWindows)
            SyncStartWithWindows();
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PreviousPageCommand.Execute(null);
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NextPageCommand.Execute(null);
    }

    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPageCommand.Execute(null);
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddProfileCommand.Execute(null);
    }

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DuplicateProfileCommand.Execute(null);
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        string? renamed = TextPromptDialog.Show(
            this,
            "Rename Profile",
            "Enter a new profile name:",
            _viewModel.ActiveProfileName,
            maxLength: 48);

        if (!string.IsNullOrWhiteSpace(renamed))
            _viewModel.RenameProfileCommand.Execute(renamed);
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanRemoveProfile)
            return;

        var result = System.Windows.MessageBox.Show(
            this,
            $"Remove profile \"{_viewModel.ActiveProfileName}\"?\n\nThis removes the profile with its pages, buttons, and notes.",
            "Remove Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
            _viewModel.RemoveProfileCommand.Execute(null);
    }

    private void RemovePage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsViewingVirtualLayout)
            return;

        if (_viewModel.PageCount <= 1) return;
        var result = System.Windows.MessageBox.Show(
            $"Remove page \"{_viewModel.CurrentPageName}\"?",
            "Remove Page", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            _viewModel.RemovePageCommand.Execute(null);
    }

    private void AddVirtualLayout_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddVirtualLayoutCommand.Execute(null);
    }

    private void RemoveVirtualLayout_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanRemoveCurrentVirtualLayout)
            return;

        var result = System.Windows.MessageBox.Show(
            $"Remove virtual layout \"{_viewModel.CurrentPageName}\"?",
            "Remove Virtual Layout", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            _viewModel.RemoveVirtualLayoutCommand.Execute(null);
    }

    private void ExitVirtualLayout_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ExitVirtualLayoutCommand.Execute(null);
    }

    private void AddNotePage_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddNotePageCommand.Execute(null);

        var popup = GetNotesAreasPopup();
        if (popup != null)
            popup.IsOpen = false;
    }

    private void RemoveNotePage_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanRemoveNotePage)
            return;

        int noteCount = _viewModel.CurrentNotePageNoteCount;
        string message = noteCount > 0
            ? $"Remove notes area \"{_viewModel.CurrentNotePageName}\"?\n\nThis will permanently delete {noteCount} sticky note(s) in this area."
            : $"Remove notes area \"{_viewModel.CurrentNotePageName}\"?";

        var result = System.Windows.MessageBox.Show(
            this,
            message,
            "Remove Notes Area",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.RemoveNotePageCommand.Execute(null);

            var popup = GetNotesAreasPopup();
            if (popup != null)
                popup.IsOpen = false;
        }
    }

    private Popup? GetNotesAreasPopup()
    {
        return FindName("NotesAreasPopup") as Popup;
    }

    private void RenameLayout_Click(object sender, RoutedEventArgs e)
    {
        string? renamed = TextPromptDialog.Show(
            this,
            "Rename Layout",
            "Enter a new name for the current layout:",
            _viewModel.CurrentPageName,
            maxLength: 48);

        if (!string.IsNullOrWhiteSpace(renamed))
            _viewModel.RenamePageCommand.Execute(renamed);
    }

    private static bool IsTextEditingControlFocused()
    {
        return Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            or System.Windows.Controls.PasswordBox
            or System.Windows.Controls.ComboBox;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsTextEditingControlFocused())
            return;

        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_viewModel.SelectedButton != null)
            {
                _viewModel.ClearButtonCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        if (e.Key == Key.C)
        {
            if (_viewModel.SelectedButton != null)
            {
                _viewModel.CopyButtonCommand.Execute(_viewModel.SelectedButton);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.V)
        {
            if (_viewModel.SelectedButton != null)
            {
                _viewModel.PasteButtonCommand.Execute(_viewModel.SelectedButton);
                e.Handled = true;
            }
        }
    }

    private void DeckEditorButton_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: ButtonViewModel buttonVm })
            _viewModel.SelectButtonCommand.Execute(buttonVm);
    }

    private void DeckEditorButton_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: ButtonViewModel buttonVm })
        {
            _viewModel.FollowNavigationTargetCommand.Execute(buttonVm);
            e.Handled = true;
        }
    }

    private void CopyButtonFromContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ButtonViewModel buttonVm })
            _viewModel.CopyButtonCommand.Execute(buttonVm);
    }

    private void PasteButtonFromContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ButtonViewModel buttonVm })
            _viewModel.PasteButtonCommand.Execute(buttonVm);
    }

    private static string? ShowColorDialog(string currentHex)
    {
        var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true
        };

        try
        {
            var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(currentHex);
            dlg.Color = System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
        }
        catch { }

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            return $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        }
        return null;
    }


    protected override void OnClosed(EventArgs e)
    {
        DisposeGamepadTogglePolling();
        _viewModel.Dispose();
        DisposeTrayIcon();
        OverlayInterop.UnregisterGlobalHotkey(this, HOTKEY_ID);
        _hwndSource?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    // Title bar handlers
    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
        }
        else if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}