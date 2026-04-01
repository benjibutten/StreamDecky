using System.Runtime.InteropServices;

namespace StreamDecky.Helpers;

/// <summary>
/// Simulates keyboard input using the Win32 SendInput API.
/// Unlike SendKeys, this works with games (DirectInput/Raw Input).
/// </summary>
public static class InputSimulator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint MAPVK_VK_TO_VSC = 0;

    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12; // Alt

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion

    private static readonly Dictionary<string, ushort> SpecialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
        ["TAB"] = 0x09,
        ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
        ["BS"] = 0x08, ["BKSP"] = 0x08, ["BACKSPACE"] = 0x08,
        ["DEL"] = 0x2E, ["DELETE"] = 0x2E,
        ["INS"] = 0x2D, ["INSERT"] = 0x2D,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PGUP"] = 0x21,
        ["PGDN"] = 0x22,
        ["UP"] = 0x26,
        ["DOWN"] = 0x28,
        ["LEFT"] = 0x25,
        ["RIGHT"] = 0x27,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["CAPSLOCK"] = 0x14,
        ["NUMLOCK"] = 0x90,
        ["SCROLLLOCK"] = 0x91,
        ["PRTSC"] = 0x2C,
        ["BREAK"] = 0x13,
        [" "] = 0x20,
    };

    /// <summary>
    /// Send key presses via SendInput, parsing SendKeys format.
    /// Supports: ^=Ctrl, +=Shift, %=Alt, {KEY} for special keys.
    /// Uses scan-code-only mode for game compatibility.
    /// Sends key down/up separately with delays so games register the press.
    /// </summary>
    public static async Task SendKeyPressAsync(string sendKeysFormat)
    {
        if (string.IsNullOrEmpty(sendKeysFormat)) return;

        int i = 0;
        bool ctrlDown = false, shiftDown = false, altDown = false;

        while (i < sendKeysFormat.Length)
        {
            char c = sendKeysFormat[i];

            if (c == '^') { ctrlDown = true; i++; continue; }
            if (c == '+') { shiftDown = true; i++; continue; }
            if (c == '%') { altDown = true; i++; continue; }
            if (c == '~')
            {
                SendModifiersDown(ctrlDown, shiftDown, altDown);
                await PressKeyAsync(0x0D);
                SendModifiersUp(ref ctrlDown, ref shiftDown, ref altDown);
                i++;
                continue;
            }

            // Press modifiers before the key
            SendModifiersDown(ctrlDown, shiftDown, altDown);

            if (c == '{')
            {
                int end = sendKeysFormat.IndexOf('}', i + 1);
                if (end > i)
                {
                    string keyName = sendKeysFormat.Substring(i + 1, end - i - 1);

                    // Check for repeat syntax like {KEY N}
                    int spaceIdx = keyName.LastIndexOf(' ');
                    int repeatCount = 1;
                    string actualKeyName = keyName;
                    if (spaceIdx > 0 && int.TryParse(keyName.Substring(spaceIdx + 1), out int count))
                    {
                        actualKeyName = keyName.Substring(0, spaceIdx);
                        repeatCount = Math.Max(1, count);
                    }

                    if (SpecialKeys.TryGetValue(actualKeyName, out ushort vk))
                    {
                        for (int r = 0; r < repeatCount; r++)
                            await PressKeyAsync(vk);
                    }
                    else if (actualKeyName.Length == 1)
                    {
                        for (int r = 0; r < repeatCount; r++)
                            await PressCharAsync(actualKeyName[0]);
                    }
                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }
            else if (c == '(' || c == ')')
            {
                if (c == ')')
                    SendModifiersUp(ref ctrlDown, ref shiftDown, ref altDown);
                i++;
                continue;
            }
            else
            {
                await PressCharAsync(c);
                i++;
            }

            SendModifiersUp(ref ctrlDown, ref shiftDown, ref altDown);
        }

        // Release any remaining modifiers
        if (ctrlDown) SendScanCodeUp(VK_CONTROL);
        if (shiftDown) SendScanCodeUp(VK_SHIFT);
        if (altDown) SendScanCodeUp(VK_MENU);
    }

    /// <summary>
    /// Synchronous wrapper for backward compatibility.
    /// </summary>
    public static void SendKeyPress(string sendKeysFormat)
    {
        SendKeyPressAsync(sendKeysFormat).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Type text using SendInput with unicode character events (works in games).
    /// </summary>
    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        var inputArray = inputs.ToArray();
        SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Send Enter key via SendInput (scan code mode).
    /// </summary>
    public static async Task SendEnterAsync()
    {
        await PressKeyAsync(0x0D);
    }

    public static void SendEnter()
    {
        SendEnterAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Send Ctrl+V (paste) via SendInput (scan code mode).
    /// </summary>
    public static async Task SendPasteAsync()
    {
        SendScanCodeDown(VK_CONTROL);
        await Task.Delay(30);
        await PressKeyAsync(0x56 /* V */);
        SendScanCodeUp(VK_CONTROL);
    }

    public static void SendPaste()
    {
        SendPasteAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Press a key down, hold for a short duration, then release.
    /// Games need time between down and up to register the press.
    /// </summary>
    private static async Task PressKeyAsync(ushort vk, int holdMs = 50)
    {
        SendScanCodeDown(vk);
        await Task.Delay(holdMs);
        SendScanCodeUp(vk);
    }

    /// <summary>
    /// Press a character key (resolves VK from char, handles shift if needed).
    /// </summary>
    private static async Task PressCharAsync(char c)
    {
        short vkResult = VkKeyScan(c);
        if (vkResult == -1)
        {
            // Can't map to VK, use unicode input
            var down = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } }
            };
            var up = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
            };
            SendInput(1, [down], Marshal.SizeOf<INPUT>());
            await Task.Delay(50);
            SendInput(1, [up], Marshal.SizeOf<INPUT>());
            return;
        }

        ushort vk = (ushort)(vkResult & 0xFF);
        bool needShift = (vkResult & 0x100) != 0;

        if (needShift) SendScanCodeDown(VK_SHIFT);
        await PressKeyAsync(vk);
        if (needShift) SendScanCodeUp(VK_SHIFT);
    }

    private static void SendModifiersDown(bool ctrl, bool shift, bool alt)
    {
        if (ctrl) SendScanCodeDown(VK_CONTROL);
        if (shift) SendScanCodeDown(VK_SHIFT);
        if (alt) SendScanCodeDown(VK_MENU);
    }

    private static void SendModifiersUp(ref bool ctrl, ref bool shift, ref bool alt)
    {
        if (alt) { SendScanCodeUp(VK_MENU); alt = false; }
        if (shift) { SendScanCodeUp(VK_SHIFT); shift = false; }
        if (ctrl) { SendScanCodeUp(VK_CONTROL); ctrl = false; }
    }

    /// <summary>
    /// Send a single scan code key-down event.
    /// Uses KEYEVENTF_SCANCODE so games (DirectInput/Raw Input) see it.
    /// </summary>
    private static void SendScanCodeDown(ushort vk)
    {
        ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = KEYEVENTF_SCANCODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Send a single scan code key-up event.
    /// </summary>
    private static void SendScanCodeUp(ushort vk)
    {
        ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }
}
