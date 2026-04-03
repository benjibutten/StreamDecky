using System.Windows;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using StreamDecky.ViewModels;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace StreamDecky.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
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

    private void ExportLayout_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Layout",
            Filter = "JSON files|*.json|All files|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"streamdecky-layout-{DateTime.Now:yyyyMMdd-HHmm}.json"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var json = JsonSerializer.Serialize(_viewModel.Profile, ExportJsonOptions);
            File.WriteAllText(dlg.FileName, json);

            System.Windows.MessageBox.Show(
                $"Layout exported to:\n{dlg.FileName}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not export layout.\n\n{ex.Message}",
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
}
