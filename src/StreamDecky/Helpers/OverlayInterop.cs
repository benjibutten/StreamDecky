using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StreamDecky.Helpers;

public static class OverlayInterop
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const uint ASFW_ANY = unchecked((uint)-1);

    // Without MOD_NOREPEAT, holding the hotkey down makes Windows post repeated
    // WM_HOTKEY messages (keyboard auto-repeat), which would toggle the overlay
    // open/closed several times per press and make it look like it flickers or
    // "didn't activate". This flag delivers exactly one message per physical press.
    private const uint MOD_NOREPEAT = 0x4000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_RESTORE = 9;

    public static void MakeTopmost(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    public static void ForceFocus(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// Lets another process take the foreground on our behalf. Windows only grants
    /// SetForegroundWindow to the process that received the last user input, which
    /// is the freshly launched second instance, not the running one it hands off to.
    /// </summary>
    public static void AllowAnyProcessToSetForeground()
    {
        AllowSetForegroundWindow(ASFW_ANY);
    }

    public static bool RegisterGlobalHotkey(Window window, int id, uint modifiers, uint vk)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        bool registered = RegisterHotKey(hwnd, id, modifiers | MOD_NOREPEAT, vk);
        if (!registered)
        {
            AppDiagnostics.Warning(
                $"Failed to register global hotkey (id={id}, modifiers=0x{modifiers:X}, vk=0x{vk:X}, lastError={Marshal.GetLastWin32Error()}). " +
                "Another application may already own this key combination.");
        }

        return registered;
    }

    public static void UnregisterGlobalHotkey(Window window, int id)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        UnregisterHotKey(hwnd, id);
    }

    public static IntPtr GetCurrentForegroundWindow()
    {
        return GetForegroundWindow();
    }

    /// <summary>
    /// Aggressively sets the foreground window using AttachThreadInput trick.
    /// This bypasses Windows' restrictions on SetForegroundWindow.
    /// </summary>
    public static void ForceSetForegroundWindow(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero) return;

        IntPtr currentForeground = GetForegroundWindow();
        uint currentThreadId = GetCurrentThreadId();
        uint foregroundThreadId = GetWindowThreadProcessId(currentForeground, out _);
        uint targetThreadId = GetWindowThreadProcessId(targetHwnd, out _);

        // Attach to the foreground thread so we have permission to change focus
        if (currentThreadId != foregroundThreadId)
            AttachThreadInput(currentThreadId, foregroundThreadId, true);
        if (currentThreadId != targetThreadId)
            AttachThreadInput(currentThreadId, targetThreadId, true);

        // Only restore minimized targets: SW_RESTORE would also un-maximize a
        // maximized window right before the text is typed into it.
        if (IsIconic(targetHwnd))
            ShowWindow(targetHwnd, SW_RESTORE);
        BringWindowToTop(targetHwnd);
        SetForegroundWindow(targetHwnd);

        // Detach threads
        if (currentThreadId != foregroundThreadId)
            AttachThreadInput(currentThreadId, foregroundThreadId, false);
        if (currentThreadId != targetThreadId)
            AttachThreadInput(currentThreadId, targetThreadId, false);
    }
}
