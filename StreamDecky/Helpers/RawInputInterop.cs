using System.Runtime.InteropServices;

namespace StreamDecky.Helpers;

/// <summary>
/// Raw Input (WM_INPUT) plumbing for the overlay hotkey fallback. Raw input is
/// delivered straight from the keyboard driver stack to every registered
/// listener, so games that suppress RegisterHotKey/WM_HOTKEY while focused
/// cannot block it.
/// </summary>
public static class RawInputInterop
{
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const ushort RI_KEY_BREAK = 0x0001;
    private const ushort RI_KEY_E0 = 0x0002;

    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;

    // Scan code of the right shift key; raw input reports both shift keys as
    // the generic VK_SHIFT and only the make code tells them apart.
    private const ushort SC_RSHIFT = 0x36;

    private static readonly int[] ModifierVirtualKeys =
    {
        VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN
    };

    // Keyboard driver escape value carrying no key information.
    private const ushort VK_NONE = 0xFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTKEYBOARD
    {
        public RAWINPUTHEADER header;
        public RAWKEYBOARD keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, out RAWINPUTKEYBOARD pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Registers the window to receive WM_INPUT for all keyboard input,
    /// including while another window (such as a game) has focus.
    /// </summary>
    public static bool RegisterKeyboardSink(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var devices = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage = HID_USAGE_GENERIC_KEYBOARD,
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = hwnd
            }
        };

        bool registered = RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        if (!registered)
        {
            AppDiagnostics.Warning(
                $"Failed to register the raw-input keyboard sink (lastError={Marshal.GetLastWin32Error()}). " +
                "The overlay hotkey may not work while games that block global hotkeys are focused.");
        }

        return registered;
    }

    public static void UnregisterKeyboardSink()
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage = HID_USAGE_GENERIC_KEYBOARD,
                dwFlags = RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            }
        };

        RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// <summary>
    /// Extracts the keyboard event from a WM_INPUT lParam. Returns false for
    /// non-keyboard input or driver escape events that carry no key. Generic
    /// modifier codes are normalized to their left/right variants so each
    /// physical modifier key can be tracked separately.
    /// </summary>
    public static bool TryGetKeyboardEvent(IntPtr lParam, out uint vk, out bool isKeyDown)
    {
        vk = 0;
        isKeyDown = false;

        uint size = (uint)Marshal.SizeOf<RAWINPUTKEYBOARD>();
        uint copied = GetRawInputData(lParam, RID_INPUT, out RAWINPUTKEYBOARD data, ref size,
            (uint)Marshal.SizeOf<RAWINPUTHEADER>());

        if (copied == unchecked((uint)-1) || data.header.dwType != RIM_TYPEKEYBOARD)
            return false;

        if (data.keyboard.VKey == VK_NONE)
            return false;

        vk = NormalizeVirtualKey(data.keyboard.VKey, data.keyboard.MakeCode, data.keyboard.Flags);
        isKeyDown = (data.keyboard.Flags & RI_KEY_BREAK) == 0;
        return true;
    }

    private static uint NormalizeVirtualKey(ushort vKey, ushort makeCode, ushort flags)
    {
        bool isExtended = (flags & RI_KEY_E0) != 0;
        return vKey switch
        {
            VK_SHIFT => makeCode == SC_RSHIFT ? VK_RSHIFT : VK_LSHIFT,
            VK_CONTROL => isExtended ? VK_RCONTROL : VK_LCONTROL,
            VK_MENU => isExtended ? VK_RMENU : VK_LMENU,
            _ => vKey
        };
    }

    /// <summary>
    /// Virtual keys of the modifier keys physically held right now. Used only
    /// to seed the matcher's tracked state at configure time; live matching
    /// follows the raw-input stream so queued events keep the modifiers they
    /// were pressed with.
    /// </summary>
    public static IReadOnlyList<uint> GetPressedModifierVirtualKeys()
    {
        var pressed = new List<uint>();
        foreach (int vk in ModifierVirtualKeys)
        {
            if (IsKeyDown(vk))
                pressed.Add((uint)vk);
        }

        return pressed;
    }

    /// <summary>
    /// Whether the given key is physically held right now. Used only to seed
    /// the matcher's state at configure time, like
    /// <see cref="GetPressedModifierVirtualKeys"/>.
    /// </summary>
    public static bool IsKeyPressed(uint vk)
    {
        return vk != 0 && IsKeyDown((int)vk);
    }

    private static bool IsKeyDown(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }
}
