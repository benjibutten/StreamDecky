using System.Runtime.InteropServices;
using StreamDecky.Helpers;
using StreamDecky.Services;

namespace StreamDecky.SpeedReader;

/// <summary>
/// Reads the text selected in the foreground app by sending it Ctrl+C. The copied
/// text stays on the clipboard, as after a manual copy.
/// </summary>
internal static class SelectedTextCapture
{
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    /// <summary>
    /// Copies the current selection and returns it, or null when nothing was copied:
    /// no selection, or an app that ignores Ctrl+C. Must run on an STA thread.
    /// </summary>
    public static async Task<string?> CaptureAsync()
    {
        // The hotkey's own modifiers are usually still held. Copying now would send
        // e.g. Ctrl+Alt+C, which apps either ignore or bind to something else.
        await WaitUntilAsync(() => RawInputInterop.GetPressedModifierVirtualKeys().Count == 0, ModifierReleaseTimeout);

        uint sequenceBefore = GetClipboardSequenceNumber();
        await InputActionGate.RunAsync(InputSimulator.SendCopyAsync);

        if (!await WaitUntilAsync(() => GetClipboardSequenceNumber() != sequenceBefore, CopyTimeout))
            return null;

        return await ReadClipboardTextAsync();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                return false;

            await Task.Delay(PollInterval);
        }

        return true;
    }

    // The copying app can still hold the clipboard open for a moment after the
    // sequence number changes.
    private static async Task<string?> ReadClipboardTextAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                await Task.Delay(40);
            }
        }

        return null;
    }
}
