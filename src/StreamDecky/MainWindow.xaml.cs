using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using StreamDecky.Admin;
using StreamDecky.Helpers;
using StreamDecky.Services;
using StreamDecky.Updates;
using StreamDecky.ViewModels;
using StreamDecky.Views;

namespace StreamDecky;

using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;

public partial class MainWindow : Window
{
    private const string EditorButtonDragFormat = "StreamDecky.EditorButton";
    private System.Windows.Point _buttonDragStart;
    private ButtonViewModel? _buttonDragSource;
    private readonly MainViewModel _viewModel = new();
    private readonly bool _startHiddenInTray;
    private const int HOTKEY_ID = 9000;
    private static readonly TimeSpan GamepadToggleCooldown = TimeSpan.FromMilliseconds(350);
    private HwndSource? _hwndSource;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private readonly OverlayWindowController _overlayController;
    private readonly HotkeyRegistrationController _hotkeyController = new();
    private readonly RawInputHotkeyMatcher _rawInputHotkeyMatcher = new();
    // The raw-input fallback only runs when we actually own the hotkey via
    // RegisterHotKey; otherwise it would bypass another app's ownership of
    // the same combination and both apps would react to it.
    private bool _globalHotkeyRegistered;
    private bool _rawInputSinkRegistered;
    private readonly StartupRegistrySyncService _startupRegistrySyncService = new();
    private readonly StartupTaskService _startupTaskService = new();
    private int _lastWarnedElevatedProcessId;
    // Keep this short: it bounds the added latency between pressing the gamepad
    // combo and the overlay toggling. The packet-number check in the tick handler
    // keeps idle polling cheap.
    private readonly System.Windows.Threading.DispatcherTimer _gamepadToggleTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(30)
    };
    private ushort _previousGamepadButtons;
    private uint _previousGamepadPacketNumber;
    private DateTime _nextGamepadToggleAllowedAtUtc = DateTime.MinValue;

    public MainWindow()
        : this(HasStartHiddenInTrayArgument(Environment.GetCommandLineArgs()))
    {
    }

    public MainWindow(bool startHiddenInTray)
    {
        _startHiddenInTray = startHiddenInTray;
        DataContext = _viewModel;
        InitializeComponent();
        VersionText.Text = AppVersion.DisplayText;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        InitializeTrayIcon();
        _overlayController = new OverlayWindowController(_viewModel);
        SyncStartWithWindows();
        InputActionGate.KeysSentToElevatedProgram += OnKeysSentToElevatedProgram;

        if (_startHiddenInTray)
            ShowInTaskbar = false;

        _gamepadToggleTimer.Tick += GamepadToggleTimer_Tick;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateGamepadTogglePolling();
    }

    private static bool HasStartHiddenInTrayArgument(string[] args)
    {
        return App.HasStartHiddenInTrayArgument(args);
    }

    private void SupportLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        ShellLauncher.Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        AboutDialog.Show(
            this,
            "StreamDecky",
            "https://github.com/benjibutten/StreamDecky");
    }

    public void StartHiddenInTray()
    {
        ShowInTaskbar = false;
        new WindowInteropHelper(this).EnsureHandle();
        HideToTray();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateEditorPanelLayoutConstraints();
        // Loaded waits for the first Show, so a start hidden in the tray offers the
        // notes when the user first opens the window.
        Dispatcher.BeginInvoke(OfferWhatsNew, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// Shows the release notes of this version once, after an update. A first run only
    /// records the version, so the notes wait for the next update.
    /// </summary>
    private void OfferWhatsNew()
    {
        if (AppVersion.Current is not { } current || _viewModel.WhatsNewShownForVersion == current.ToString())
            return;

        // Profiles are saved only once something changes, so a launch by an update also
        // counts as set up.
        bool isSetUp = !_viewModel.IsFirstRun || UpdateInstaller.IsRestartAfterUpdate(Environment.GetCommandLineArgs());
        if (WhatsNewDialog.ShouldShow(current, _viewModel.WhatsNewShownForVersion, isSetUp)
            && WhatsNewDialog.ReadBuiltInItems().Count > 0)
        {
            WhatsNewDialog.Show(this);
        }

        _viewModel.WhatsNewShownForVersion = current.ToString();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    private void MainContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateEditorPanelLayoutConstraints();
    }

    private void EditorPanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        UpdateEditorPanelLayoutConstraints();
    }

    private void UpdateEditorPanelLayoutConstraints()
    {
        if (!IsLoaded || MainContentGrid.ActualWidth <= 0)
            return;

        double splitterWidth = EditorSplitterColumn.ActualWidth;
        double minimumDeckWidth = DeckColumn.MinWidth;
        double minimumEditorWidth = EditorColumn.MinWidth;
        double maximumEditorWidth = MainContentGrid.ActualWidth - splitterWidth - minimumDeckWidth;

        if (maximumEditorWidth <= 0)
            return;

        if (maximumEditorWidth < minimumEditorWidth)
            maximumEditorWidth = minimumEditorWidth;

        EditorColumn.MaxWidth = maximumEditorWidth;

        double currentEditorWidth = EditorColumn.ActualWidth;
        if (currentEditorWidth <= 0)
            currentEditorWidth = EditorColumn.Width.IsAbsolute ? EditorColumn.Width.Value : minimumEditorWidth;

        double clampedEditorWidth = Math.Clamp(currentEditorWidth, minimumEditorWidth, maximumEditorWidth);
        EditorColumn.Width = new GridLength(clampedEditorWidth, GridUnitType.Pixel);
        DeckColumn.Width = new GridLength(1, GridUnitType.Star);
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
        contextMenu.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
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
        ShowAndActivate();
    }

    private static void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppDiagnostics.LogDirectory);
            ShellLauncher.Open(AppDiagnostics.LogDirectory);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Failed to open the log folder.", ex);
        }
    }

    /// <summary>
    /// Registration fails silently at the Win32 level when another app owns the
    /// combination; without this the overlay just looks broken.
    /// </summary>
    private void NotifyIfHotkeyUnavailable()
    {
        if (_globalHotkeyRegistered || _trayIcon == null)
            return;

        _trayIcon.ShowBalloonTip(
            5000,
            "StreamDecky hotkey unavailable",
            "Another application already uses the overlay hotkey. Choose a different combination in Settings.",
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    public void ShowAndActivate()
    {
        // An open overlay re-asserts topmost on deactivation and would sit as a
        // dimmed, click-eating sheet over the editor we are about to show.
        if (_overlayController.IsOpen)
            _overlayController.Toggle();

        // Restore before Show so a window hidden while minimized does not first
        // appear minimized and then animate up.
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Show();
        Activate();

        // Toggle Topmost once to bring a hidden/minimized window to the foreground reliably.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void HideToTray()
    {
        // Hide first: WPF applies a ShowInTaskbar change to a visible window by
        // hiding and re-showing it, which flashes the window on its way out.
        Hide();
        ShowInTaskbar = false;
    }

    private void ExitApplication()
    {
        InputActionGate.KeysSentToElevatedProgram -= OnKeysSentToElevatedProgram;
        DisposeGamepadTogglePolling();
        DisposeTrayIcon();
        _hotkeyController.Unregister(this, HOTKEY_ID);
        RawInputInterop.UnregisterKeyboardSink();
        _hwndSource?.RemoveHook(WndProc);
        System.Windows.Application.Current.Shutdown();
    }

    internal void ExitForUpdate() => ExitApplication();

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
                _globalHotkeyRegistered = _hotkeyController.ReRegister(this, HOTKEY_ID, _viewModel.HotkeyModifiers, _viewModel.HotkeyVk);
                NotifyIfHotkeyUnavailable();
            }

            ConfigureRawInputHotkeyMatcher();
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.StartWithWindows))
            SyncStartWithWindows();

        if (e.PropertyName == nameof(MainViewModel.RunAsAdministrator))
            OnRunAsAdministratorChanged();
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
        _previousGamepadPacketNumber = 0;
    }

    private void GamepadToggleTimer_Tick(object? sender, EventArgs e)
    {
        if (!_viewModel.GamepadSupportEnabled)
            return;

        if (!XInputInterop.TryGetFirstConnectedState(out var state))
        {
            _previousGamepadButtons = 0;
            _previousGamepadPacketNumber = 0;
            return;
        }

        if (state.dwPacketNumber == _previousGamepadPacketNumber)
            return;

        _previousGamepadPacketNumber = state.dwPacketNumber;

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
        _previousGamepadPacketNumber = 0;
    }

    private void DisposeTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }

    private const string StartupHintElevated =
        "Starts as administrator when you sign in, without asking.";
    private const string StartupHintTaskPending =
        "Starts as administrator at sign-in once StreamDecky has run as administrator.";
    private const string StartupHintTaskPendingRemoval =
        "Still starts as administrator at sign-in until StreamDecky runs as administrator again.";
    private const string StartupHintNotProtected =
        "From this folder, StreamDecky starts without administrator rights at sign-in. Installed with the StreamDecky installer, it starts as administrator at sign-in too.";
    private const string StartupHintNoAdministratorAccount =
        "Running as administrator needs a Windows account with administrator rights.";

    /// <summary>
    /// Mirrors "Start with Windows" into whichever of the two startup mechanisms
    /// applies, and never both: StreamDecky started twice at sign-in would have the
    /// second copy hand over to the first.
    /// </summary>
    private void SyncStartWithWindows()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return;

        bool runAsAdministrator = _viewModel.RunAsAdministrator;
        bool startElevated = _viewModel.StartWithWindows && runAsAdministrator;
        // Starting as administrator with no prompt needs a launcher and an exe that nothing
        // without administrator rights can swap out; from anywhere else it would hand those
        // rights to whoever replaced a file.
        bool isProtected = runAsAdministrator
            && Elevation.IsProtectedFromNonAdministrators(Elevation.LauncherPath)
            && Elevation.IsProtectedFromNonAdministrators(exePath);
        string hint = string.Empty;

        try
        {
            if (Elevation.IsElevated)
            {
                bool useTask = startElevated && isProtected;
                try
                {
                    _startupTaskService.Sync(useTask, Elevation.LauncherPath);
                }
                catch (Exception ex)
                {
                    // Task Scheduler can be turned off or blocked by policy; the Run key still starts StreamDecky.
                    AppDiagnostics.Warning("Could not update the startup task; using the Run key instead.", ex);
                    useTask = false;
                }

                SyncRunKey(_viewModel.StartWithWindows && !useTask, exePath);
                hint = useTask ? StartupHintElevated : string.Empty;
            }
            else if (_startupTaskService.Exists())
            {
                // Registered by StreamDecky running as administrator; only such a copy may change it.
                SyncRunKey(false, exePath);
                hint = startElevated ? StartupHintElevated : StartupHintTaskPendingRemoval;
            }
            else
            {
                SyncRunKey(_viewModel.StartWithWindows, exePath);
                hint = startElevated && isProtected ? StartupHintTaskPending : string.Empty;
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Failed to synchronize the Start with Windows setting.", ex);
        }

        if (startElevated && !isProtected)
            hint = StartupHintNotProtected;

        if (!Elevation.CanRunAsAdministrator)
            hint = StartupHintNoAdministratorAccount;

        _viewModel.StartupHint = hint;
    }

    private void SyncRunKey(bool startWithWindows, string exePath)
    {
        if (!_startupRegistrySyncService.Sync(startWithWindows, exePath))
            AppDiagnostics.Warning("Could not open the Windows startup registry key.");
    }

    private void OnRunAsAdministratorChanged()
    {
        SyncStartWithWindows();
        // After the checkbox has taken the new value, which RestartAsAdministrator may undo.
        if (_viewModel.RunAsAdministrator && !Elevation.IsElevated)
            Dispatcher.BeginInvoke(RestartAsAdministrator);
    }

    private void RestartAsAdministrator()
    {
        try
        {
            if (Elevation.TryStartElevatedCopy([]))
            {
                ExitApplication();
                return;
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Could not restart StreamDecky as administrator.", ex);
        }

        // Declined or failed: leaving the setting on would ask again at every launch.
        _viewModel.RunAsAdministrator = false;
    }

    private void OnKeysSentToElevatedProgram(string programName, int processId)
    {
        // Once per program run: every button press would otherwise repeat it.
        if (Interlocked.Exchange(ref _lastWarnedElevatedProcessId, processId) == processId)
            return;

        Dispatcher.BeginInvoke(() => _trayIcon?.ShowBalloonTip(
            8000,
            "Keys blocked by Windows",
            $"{programName} is running as administrator, so Windows drops the keys StreamDecky sends to it. "
                + "Turn on \"Run StreamDecky as administrator\" in Settings > General.",
            System.Windows.Forms.ToolTipIcon.Warning));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        _globalHotkeyRegistered = _hotkeyController.Register(this, HOTKEY_ID, _viewModel.HotkeyModifiers, _viewModel.HotkeyVk);
        NotifyIfHotkeyUnavailable();

        // Fallback path: some games (raw-input titles like Doom: The Dark Ages)
        // suppress WM_HOTKEY delivery while they have focus. The raw-input sink
        // still receives every keystroke, so the hotkey keeps working there.
        _rawInputSinkRegistered = RawInputInterop.RegisterKeyboardSink(hwnd);
        ConfigureRawInputHotkeyMatcher();

        if (_startHiddenInTray)
            HideToTray();
    }

    private void ConfigureRawInputHotkeyMatcher()
    {
        _rawInputHotkeyMatcher.Configure(
            _viewModel.HotkeyModifiers,
            _viewModel.HotkeyVk,
            RawInputInterop.GetPressedModifierVirtualKeys(),
            RawInputInterop.IsKeyPressed(_viewModel.HotkeyVk));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        const int WM_INPUT = 0x00FF;

        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            // The raw-input path may already have toggled for this physical
            // press; the matcher claims each press exactly once. Without a
            // working sink there is no duplicate source, so toggle directly.
            if (!_rawInputSinkRegistered || _rawInputHotkeyMatcher.TryHandleHotkeyMessage())
                ToggleOverlay();

            handled = true;
        }
        else if (msg == WM_INPUT)
        {
            // Only act when we own the hotkey via RegisterHotKey. If another
            // app owns the combination, registration failed and reacting here
            // anyway would ignore that ownership.
            if (_globalHotkeyRegistered
                && RawInputInterop.TryGetKeyboardEvent(lParam, out uint vk, out bool isKeyDown)
                && _rawInputHotkeyMatcher.ProcessKeyEvent(vk, isKeyDown))
            {
                ToggleOverlay();
            }
            // Leave handled = false so WPF runs DefWindowProc, which performs
            // the required WM_INPUT cleanup.
        }
        return IntPtr.Zero;
    }

    private void ToggleOverlay()
    {
        _overlayController.Toggle();
    }

    private void OpenOverlay()
    {
        _overlayController.Open();
    }

    private void OpenOverlay_Click(object sender, RoutedEventArgs e)
    {
        ToggleOverlay();
    }

    /// <summary>Opens the ⋯ button's context menu under the button, as a left click.</summary>
    private void OpenMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } button)
            return;

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }

    private void ToggleClipboardSettingsPopup_Click(object sender, RoutedEventArgs e)
    {
        var popup = ClipboardSettingsPopup;
        if (popup != null)
            popup.IsOpen = !popup.IsOpen;
    }

    private void InsertFormToken_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: string token } || string.IsNullOrWhiteSpace(token))
            return;

        var box = FormOutputTemplateBox;
        if (box == null)
            return;

        string insert = "{" + token + "}";
        int caret = box.CaretIndex;
        box.Text = box.Text.Insert(caret, insert);
        box.CaretIndex = caret + insert.Length;
        box.Focus();
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
        var color = ColorPickerDialog.Show(this, _viewModel.SelectedButton.BackgroundColor);
        if (color != null)
            _viewModel.SelectedButton.BackgroundColor = color;
    }

    private void BtnTextColorPicker_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedButton == null) return;
        var color = ColorPickerDialog.Show(this, _viewModel.SelectedButton.TextColor);
        if (color != null)
            _viewModel.SelectedButton.TextColor = color;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var oldStartup = _viewModel.StartWithWindows;

        var settingsWindow = new SettingsWindow(_viewModel);
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();

        // Hotkey changes are re-registered by ViewModel_PropertyChanged as the
        // recorder updates the view model, which also keeps the raw-input
        // ownership flag in sync; re-registering here would lose that result.

        // Sync startup setting if changed
        if (oldStartup != _viewModel.StartWithWindows)
            SyncStartWithWindows();
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
            this,
            $"Remove page \"{_viewModel.CurrentPageName}\"?\n\nThis permanently removes the page and all of its button assignments.",
            "Remove Page",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            _viewModel.RemovePageCommand.Execute(null);
    }

    private void SetPageTabColor_Click(object sender, RoutedEventArgs e)
    {
        string current = _viewModel.HasCurrentPageTabColor
            ? _viewModel.CurrentPageTabColor
            : _viewModel.PageTabActiveColor;
        var color = ColorPickerDialog.Show(this, current);
        if (color != null)
            _viewModel.CurrentPageTabColor = color;
    }

    private void ClearPageTabColor_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CurrentPageTabColor = string.Empty;
    }

    private void RemoveLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsViewingVirtualLayout)
            RemoveVirtualLayout_Click(sender, e);
        else
            RemovePage_Click(sender, e);
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
            this,
            $"Remove virtual layout \"{_viewModel.CurrentPageName}\"?\n\nThis permanently removes the virtual layout and all of its button assignments.",
            "Remove Virtual Layout",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
            _viewModel.RemoveVirtualLayoutCommand.Execute(null);
    }

    private void ExitVirtualLayout_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ExitVirtualLayoutCommand.Execute(null);
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

    private void RenameQuickTextCategory_Click(object sender, RoutedEventArgs e)
    {
        string? renamed = TextPromptDialog.Show(
            this,
            "Rename Clipboard Tag",
            "Enter a tag name:",
            _viewModel.CurrentQuickTextCategoryName,
            maxLength: 48);

        if (!string.IsNullOrWhiteSpace(renamed))
            _viewModel.RenameQuickTextCategoryCommand.Execute(renamed);
    }

    private void RenameQuickTextCollection_Click(object sender, RoutedEventArgs e)
    {
        string? renamed = TextPromptDialog.Show(
            this,
            "Rename Clipboard Collection",
            "Enter a collection name:",
            _viewModel.CurrentQuickTextCollectionName,
            maxLength: 48);

        if (!string.IsNullOrWhiteSpace(renamed))
            _viewModel.RenameQuickTextCollectionCommand.Execute(renamed);
    }

    private void RenameFormTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedFormTemplate is not { } template)
            return;

        string? renamed = TextPromptDialog.Show(
            this,
            "Rename Form",
            "Enter a form name:",
            template.Name,
            maxLength: 48);

        if (!string.IsNullOrWhiteSpace(renamed))
            _viewModel.RenameFormTemplateCommand.Execute(renamed);
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
            _viewModel.SelectButtonAndShowEditorCommand.Execute(buttonVm);
    }

    private void DeckEditorButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _buttonDragStart = e.GetPosition(this);
        _buttonDragSource = (sender as FrameworkElement)?.DataContext as ButtonViewModel;
    }

    private void DeckEditorButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _buttonDragSource is not { IsConfigured: true } source)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _buttonDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _buttonDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _buttonDragSource = null;
        var data = new System.Windows.DataObject(EditorButtonDragFormat, source);
        System.Windows.DragDrop.DoDragDrop(
            (DependencyObject)sender,
            data,
            System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Copy);
    }

    private void DeckEditorButton_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        var source = e.Data.GetData(EditorButtonDragFormat) as ButtonViewModel;
        var target = (sender as FrameworkElement)?.DataContext as ButtonViewModel;
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        e.Effects = source != null && target != null && source.Index != target.Index
            ? copy ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void DeckEditorButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        var source = e.Data.GetData(EditorButtonDragFormat) as ButtonViewModel;
        var target = (sender as FrameworkElement)?.DataContext as ButtonViewModel;
        if (source == null || target == null || source.Index == target.Index)
            return;

        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

        if (target.IsConfigured)
        {
            string targetName = string.IsNullOrWhiteSpace(target.DisplayTitle)
                ? target.SlotLabel
                : $"{target.DisplayTitle} ({target.SlotLabel})";
            bool confirmed = ConfirmDialog.Show(
                this,
                "Replace button?",
                $"{(copy ? "Copying" : "Moving")} this button here will replace “{targetName}”. Continue?",
                confirmText: "Replace",
                danger: true);
            if (!confirmed)
                return;
        }

        if (copy)
            _viewModel.CopyButtonTo(source, target);
        else
            _viewModel.MoveButton(source, target);
        e.Handled = true;
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

    private void RemoveButtonFromContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ButtonViewModel buttonVm })
            return;

        _viewModel.SelectButtonAndShowEditorCommand.Execute(buttonVm);
        _viewModel.ClearButtonCommand.Execute(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeGamepadTogglePolling();
        _viewModel.Dispose();
        DisposeTrayIcon();
        _hotkeyController.Unregister(this, HOTKEY_ID);
        RawInputInterop.UnregisterKeyboardSink();
        _hwndSource?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        HideToTray();
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
        HideToTray();
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "☐";
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }
}
