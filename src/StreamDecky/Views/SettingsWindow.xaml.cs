using System.Windows;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using StreamDecky.Helpers;
using StreamDecky.Models;
using StreamDecky.Services;
using StreamDecky.ViewModels;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace StreamDecky.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private const ushort RecordableGamepadButtons =
        XInputInterop.GamepadDPadUp |
        XInputInterop.GamepadDPadDown |
        XInputInterop.GamepadDPadLeft |
        XInputInterop.GamepadDPadRight |
        XInputInterop.GamepadStart |
        XInputInterop.GamepadBack |
        XInputInterop.GamepadLeftThumb |
        XInputInterop.GamepadRightThumb |
        XInputInterop.GamepadLeftShoulder |
        XInputInterop.GamepadRightShoulder |
        XInputInterop.GamepadA |
        XInputInterop.GamepadB |
        XInputInterop.GamepadX |
        XInputInterop.GamepadY;

    private readonly System.Windows.Threading.DispatcherTimer _gamepadRecordTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(33)
    };

    private bool _hasPendingDeepSeekApiKey;
    private bool _hasPendingBraveApiKey;
    private bool _isRecordingGamepadCombo;
    private bool _hasRecordedGamepadPress;
    private ushort _recordedGamepadButtons;

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _gamepadRecordTimer.Tick += GamepadRecordTimer_Tick;

        // Closing the window with the caret still in a key box must not lose the key.
        Closing += (_, _) =>
        {
            CommitDeepSeekApiKey();
            CommitBraveApiKey();
        };
    }

    private void DeepSeekApiKeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.PasswordBox box)
            return;

        // PasswordBox cannot be bound, so the stored key is pushed in once on load
        // and pushed back on every change below. Seeding it raises PasswordChanged,
        // which must not count as an edit by the user.
        string storedKey = _viewModel.DeepSeekApiKey;
        if (string.Equals(box.Password, storedKey, StringComparison.Ordinal))
            return;

        box.Password = storedKey;
        _hasPendingDeepSeekApiKey = false;
    }

    /// <summary>
    /// Only marks the key dirty. Committing per keystroke would mean a DPAPI call and a
    /// synchronous file write for every character typed, so the write is deferred to
    /// <see cref="CommitDeepSeekApiKey"/> when focus leaves the box or the window closes.
    /// </summary>
    private void DeepSeekApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _hasPendingDeepSeekApiKey = true;
    }

    private void DeepSeekApiKeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitDeepSeekApiKey();
    }

    private void CommitDeepSeekApiKey()
    {
        if (!_hasPendingDeepSeekApiKey)
            return;

        _hasPendingDeepSeekApiKey = false;
        _viewModel.DeepSeekApiKey = DeepSeekApiKeyBox.Password;
    }

    private void BraveApiKeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.PasswordBox box)
            return;

        // Same one-way seeding as the DeepSeek box: PasswordBox cannot be bound, and
        // seeding it raises PasswordChanged, which must not count as an edit by the user.
        string storedKey = _viewModel.BraveApiKey;
        if (string.Equals(box.Password, storedKey, StringComparison.Ordinal))
            return;

        box.Password = storedKey;
        _hasPendingBraveApiKey = false;
    }

    private void BraveApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _hasPendingBraveApiKey = true;
    }

    private void BraveApiKeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitBraveApiKey();
    }

    private void CommitBraveApiKey()
    {
        if (!_hasPendingBraveApiKey)
            return;

        _hasPendingBraveApiKey = false;
        _viewModel.BraveApiKey = BraveApiKeyBox.Password;
    }

    private void OverlayBgColorPicker_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var color = ShowColorDialog(_viewModel.OverlayBackgroundColor);
        if (color != null)
            _viewModel.OverlayBackgroundColor = color;
    }

    private void SettingsTitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void SettingsClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BgImageBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Background Image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
            _viewModel.OverlayBackgroundImagePath = dlg.FileName;
    }

    private void BgImageClear_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OverlayBackgroundImagePath = string.Empty;
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Profile",
            Filter = "JSON files|*.json|All files|*.*"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            string json = File.ReadAllText(dlg.FileName);
            var importedProfile = ProfileService.DeserializeProfileJson(json);
            if (importedProfile == null)
            {
                System.Windows.MessageBox.Show(
                    "Could not import profile. File content is invalid.",
                    "Import Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _viewModel.ImportProfileCommand.Execute(importedProfile);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not import profile.\n\n{ex.Message}",
                "Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        string safeName = MakeSafeFileName(_viewModel.ActiveProfileName);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Profile",
            Filter = "JSON files|*.json|All files|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"streamdecky-profile-{safeName}-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var json = ProfileService.SerializeProfileJson(_viewModel.Profile);
            File.WriteAllText(dlg.FileName, json);

            System.Windows.MessageBox.Show(
                $"Profile exported to:\n{dlg.FileName}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not export profile.\n\n{ex.Message}",
                "Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RecordHotkey_Click(object sender, RoutedEventArgs e)
    {
        RecordHotkeyButton.Content = "⏺ Press keys...";
        RecordHotkeyButton.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5A2727"));
        PreviewKeyDown += OnHotkeyRecordKeyDown;
        Focus();
    }

    private void OnHotkeyRecordKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            StopRecordingHotkey();
            return;
        }

        var modifiers = Keyboard.Modifiers;
        uint mod = 0;
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control)) { mod |= 0x0002; parts.Add("Ctrl"); }
        if (modifiers.HasFlag(ModifierKeys.Alt))     { mod |= 0x0001; parts.Add("Alt"); }
        if (modifiers.HasFlag(ModifierKeys.Shift))   { mod |= 0x0004; parts.Add("Shift"); }
        if (modifiers.HasFlag(ModifierKeys.Windows)) { mod |= 0x0008; parts.Add("Win"); }

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        parts.Add(key.ToString());

        _viewModel.HotkeyModifiers = mod;
        _viewModel.HotkeyVk = vk;
        _viewModel.HotkeyDisplayText = string.Join(" + ", parts);

        StopRecordingHotkey();
    }

    private void StopRecordingHotkey()
    {
        PreviewKeyDown -= OnHotkeyRecordKeyDown;
        RecordHotkeyButton.Content = "⌨ Record";
        RecordHotkeyButton.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D44"));
    }

    private void RecordGamepadCombo_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecordingGamepadCombo)
        {
            StopRecordingGamepadCombo();
            return;
        }

        _recordedGamepadButtons = 0;
        _hasRecordedGamepadPress = false;
        _isRecordingGamepadCombo = true;

        RecordGamepadComboButton.Content = "⏺ Press combo...";
        RecordGamepadComboButton.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#274A5A"));

        _gamepadRecordTimer.Start();
    }

    private void GamepadRecordTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isRecordingGamepadCombo)
            return;

        if (!XInputInterop.TryGetFirstConnectedState(out var state))
        {
            if (!_hasRecordedGamepadPress)
                RecordGamepadComboButton.Content = "⏺ Waiting for controller...";
            return;
        }

        if (!_hasRecordedGamepadPress)
            RecordGamepadComboButton.Content = "⏺ Press combo...";

        ushort pressedButtons = (ushort)(state.Gamepad.wButtons & RecordableGamepadButtons);

        if (pressedButtons != 0)
        {
            _recordedGamepadButtons |= pressedButtons;
            _hasRecordedGamepadPress = true;
            return;
        }

        if (_hasRecordedGamepadPress && _recordedGamepadButtons != 0)
        {
            _viewModel.GamepadToggleButtons = _recordedGamepadButtons;
            StopRecordingGamepadCombo();
        }
    }

    private void StopRecordingGamepadCombo()
    {
        _gamepadRecordTimer.Stop();
        _isRecordingGamepadCombo = false;
        _hasRecordedGamepadPress = false;
        _recordedGamepadButtons = 0;

        RecordGamepadComboButton.Content = "🎮 Record";
        RecordGamepadComboButton.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D44"));
    }

    protected override void OnClosed(EventArgs e)
    {
        PreviewKeyDown -= OnHotkeyRecordKeyDown;
        _gamepadRecordTimer.Stop();
        _gamepadRecordTimer.Tick -= GamepadRecordTimer_Tick;
        base.OnClosed(e);
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

    private static string MakeSafeFileName(string value)
    {
        string fallback = string.IsNullOrWhiteSpace(value) ? "profile" : value.Trim();

        foreach (char c in Path.GetInvalidFileNameChars())
            fallback = fallback.Replace(c, '-');

        return fallback;
    }
}
