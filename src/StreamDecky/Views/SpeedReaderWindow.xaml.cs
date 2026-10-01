using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using StreamDecky.Helpers;
using StreamDecky.SpeedReader;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace StreamDecky.Views;

/// <summary>
/// A compact reader across the screen under the mouse that flashes text one word at a time
/// (rapid serial visual presentation), with the eye's fixation letter held in place.
/// </summary>
public partial class SpeedReaderWindow : Window
{
    private const double MaxWordFontSize = 60;

    // Gives the eye time to find the pivot before the words start moving.
    private static readonly TimeSpan LeadIn = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private List<RsvpWord> _words = [];
    private int _position;
    private int _wpm;
    private IntPtr _returnFocusTo;
    private int _verticalPositionPercent = 50;
    private System.Drawing.Rectangle _workArea;

    public SpeedReaderWindow(int wpm)
    {
        InitializeComponent();
        _wpm = SpeedReaderSettings.ClampWpm(wpm);
        _timer.Tick += Timer_Tick;
        Deactivated += Window_Deactivated;
        // A new scale, or a move to a screen with another DPI, changes the size.
        SizeChanged += (_, _) => Reposition();
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Raised when the user changes the speed with the arrow keys.</summary>
    public event Action<int>? WpmChanged;

    public bool HasWords => _words.Count > 0;

    /// <summary>
    /// Scales the whole reader and places its centre <paramref name="verticalPositionPercent"/>
    /// of the way down the screen. Applies at once when the reader is open.
    /// </summary>
    public void ApplyLayout(int scalePercent, int verticalPositionPercent)
    {
        RootScale.ScaleX = RootScale.ScaleY = SpeedReaderSettings.ClampScalePercent(scalePercent) / 100.0;
        _verticalPositionPercent = SpeedReaderSettings.ClampVerticalPositionPercent(verticalPositionPercent);
        Reposition();
    }

    private bool IsPlaying => _timer.IsEnabled;

    /// <summary>
    /// Shows the window, activates it and starts reading <paramref name="text"/> from the
    /// beginning. Without readable words it explains how to select text instead.
    /// Focus goes back to <paramref name="returnFocusTo"/> when the user closes the reader.
    /// </summary>
    public void Read(string? text, IntPtr returnFocusTo)
    {
        _returnFocusTo = returnFocusTo;
        _words = string.IsNullOrWhiteSpace(text) ? [] : Rsvp.Parse(text);
        _position = 0;
        _timer.Stop();

        bool hasWords = HasWords;
        var wordsVisibility = hasWords ? Visibility.Visible : Visibility.Collapsed;
        WordGrid.Visibility = wordsVisibility;
        ProgressRow.Visibility = wordsVisibility;
        FooterRow.Visibility = wordsVisibility;
        MessageText.Visibility = hasWords ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = "Nothing selected.\nSelect some text first, then press the hotkey.";

        BringToFront();

        if (hasWords)
            Play();
        else
            Render();
    }

    /// <summary>Shows and activates the window, continuing from where the reader stopped.</summary>
    public void Resume(IntPtr returnFocusTo)
    {
        _returnFocusTo = returnFocusTo;
        BringToFront();
        Play();
    }

    private void BringToFront()
    {
        // Chosen once per showing, so moving the mouse while reading never moves the reader.
        _workArea = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        Show();
        Reposition();
        Activate();
        OverlayInterop.MakeTopmost(this);
        OverlayInterop.ForceFocus(this);
    }

    // In physical pixels: WPF's Left and Top do not map reliably onto a monitor
    // whose DPI differs from the one the window is on.
    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!IsVisible || hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        int x = _workArea.Left + (_workArea.Width - width) / 2;
        int centreY = _workArea.Top + _workArea.Height * _verticalPositionPercent / 100;
        int y = Math.Clamp(centreY - height / 2, _workArea.Top, Math.Max(_workArea.Top, _workArea.Bottom - height));

        const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void Play()
    {
        if (!HasWords)
            return;

        if (_position >= _words.Count - 1)
            _position = 0;

        _timer.Interval = CurrentWordDuration() + LeadIn;
        _timer.Start();
        Render();
    }

    private void Pause()
    {
        _timer.Stop();
        Render();
    }

    private void TogglePlay()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_position >= _words.Count - 1)
        {
            Pause();
            return;
        }

        _position++;
        _timer.Interval = CurrentWordDuration();
        Render();
    }

    private TimeSpan CurrentWordDuration() => TimeSpan.FromMinutes(_words[_position].DelayFactor / _wpm);

    private void Step(int delta)
    {
        if (!HasWords)
            return;

        _position = Math.Clamp(_position + delta, 0, _words.Count - 1);
        Render();
    }

    private void ChangeWpm(int delta)
    {
        int wpm = SpeedReaderSettings.ClampWpm(_wpm + delta);
        if (wpm == _wpm)
            return;

        _wpm = wpm;
        WpmChanged?.Invoke(wpm);
        Render();
    }

    private void Render()
    {
        WpmText.Text = $"{_wpm} wpm";

        if (!HasWords)
        {
            StatusText.Text = string.Empty;
            return;
        }

        var word = _words[_position];
        BeforeText.Text = word.Before;
        PivotText.Text = word.Pivot;
        AfterText.Text = word.After;
        // Shrinks long words so the longer side of the pivot fits in half the stage.
        TextElement.SetFontSize(WordGrid, Math.Min(MaxWordFontSize, 0.85 * Stage.Width / (word.LongestSide + 1)));

        bool atEnd = _position == _words.Count - 1;
        StatusText.Text = IsPlaying ? "Reading"
            : atEnd ? "Done · Space to read again"
            : "Paused · Space to continue";

        ProgressScale.ScaleX = (_position + 1) / (double)_words.Count;
        PositionText.Text = $"{_position + 1:N0} / {_words.Count:N0} words";
        RemainingText.Text = $"{FormatRemaining()} left";
    }

    private string FormatRemaining()
    {
        double factors = 0;
        for (int i = _position + 1; i < _words.Count; i++)
            factors += _words[i].DelayFactor;

        var remaining = TimeSpan.FromMinutes(factors / _wpm);
        return remaining.TotalHours >= 1 ? remaining.ToString(@"h\:mm\:ss") : remaining.ToString(@"m\:ss");
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        switch (e.Key)
        {
            case Key.Space or Key.Enter when !e.IsRepeat:
                TogglePlay();
                break;
            case Key.Left:
                Step(-1);
                break;
            case Key.Right:
                Step(1);
                break;
            case Key.Up:
                ChangeWpm(SpeedReaderSettings.WpmStep);
                break;
            case Key.Down:
                ChangeWpm(-SpeedReaderSettings.WpmStep);
                break;
            case Key.Home:
                Step(-_position);
                break;
            case Key.Escape:
                Close();
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    private void Stage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TogglePlay();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // The hint has done its job once the user goes back to select text.
        if (!HasWords)
        {
            Close();
            return;
        }

        Pause();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _timer.Stop();
        Deactivated -= Window_Deactivated;

        // Only while active: after a click elsewhere the user has already chosen where focus goes.
        if (IsActive)
            OverlayInterop.ForceSetForegroundWindow(_returnFocusTo);

        base.OnClosing(e);
    }
}
