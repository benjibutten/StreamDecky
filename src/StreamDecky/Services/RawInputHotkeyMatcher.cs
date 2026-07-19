namespace StreamDecky.Services;

/// <summary>
/// Decides when a raw-input keyboard event should trigger the overlay hotkey.
/// This is the fallback path for games that suppress WM_HOTKEY delivery while
/// focused (e.g. titles registering raw input with RIDEV_NOHOTKEYS).
///
/// Modifier state is tracked from the raw-input stream itself rather than
/// sampled at processing time, so a queued event is matched against the
/// modifiers that were held when the key was actually pressed. The matcher
/// also deduplicates against WM_HOTKEY: both paths observe the same physical
/// press, and <see cref="TryHandleHotkeyMessage"/> plus the press/release
/// cycle guarantee exactly one toggle per press regardless of arrival order.
///
/// Pure state machine so it can be unit tested without user32.
/// </summary>
public sealed class RawInputHotkeyMatcher
{
    // Modifier flags matching RegisterHotKey's fsModifiers values.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private uint _modifiers;
    private uint _vk;
    // Each physical modifier key is tracked separately so holding both e.g.
    // Ctrl keys and releasing one keeps the Ctrl group pressed.
    private readonly HashSet<uint> _heldModifierVks = new();
    private bool _hotkeyKeyHeld;
    private bool _currentPressHandled;

    /// <param name="pressedModifierVks">
    /// Virtual keys of the modifier keys physically held right now. Seeds the
    /// tracked state so modifiers pressed before the matcher started
    /// listening are not missed.
    /// </param>
    /// <param name="hotkeyKeyAlreadyHeld">
    /// Whether the hotkey key itself is physically held right now. True while
    /// the user is still holding the key they just recorded as the hotkey;
    /// seeding it prevents the queued key-down or next auto-repeat from
    /// counting as a fresh press and toggling the overlay immediately.
    /// </param>
    public void Configure(uint modifiers, uint vk, IEnumerable<uint>? pressedModifierVks = null, bool hotkeyKeyAlreadyHeld = false)
    {
        _modifiers = modifiers;
        _vk = vk;
        _hotkeyKeyHeld = hotkeyKeyAlreadyHeld;
        _currentPressHandled = false;

        _heldModifierVks.Clear();
        if (pressedModifierVks != null)
        {
            foreach (uint modifierVk in pressedModifierVks)
            {
                if (GetModifierFlag(modifierVk) != 0)
                    _heldModifierVks.Add(modifierVk);
            }
        }
    }

    /// <summary>
    /// Feed one keyboard event from the raw-input stream. Returns true when
    /// the configured hotkey was freshly pressed with exactly the configured
    /// modifiers held down and no other path has handled this press yet.
    /// Keyboard auto-repeat delivers repeated key-down events without a
    /// key-up in between; those must not re-trigger (mirrors MOD_NOREPEAT).
    /// </summary>
    public bool ProcessKeyEvent(uint vk, bool isKeyDown)
    {
        if (GetModifierFlag(vk) != 0)
        {
            if (isKeyDown)
                _heldModifierVks.Add(vk);
            else
                _heldModifierVks.Remove(vk);
        }

        if (_vk == 0 || vk != _vk)
            return false;

        if (!isKeyDown)
        {
            _hotkeyKeyHeld = false;
            _currentPressHandled = false;
            return false;
        }

        if (_hotkeyKeyHeld)
            return false;

        // Held even on a non-matching press, so adding the missing modifier
        // mid-hold cannot fire on an auto-repeat like a fresh press would.
        _hotkeyKeyHeld = true;

        if (ComputePressedModifiers() != _modifiers)
            return false;

        if (_currentPressHandled)
            return false;

        _currentPressHandled = true;
        return true;
    }

    /// <summary>
    /// Claim the current physical press for a WM_HOTKEY message. Returns
    /// false when the raw-input path already toggled for this press; the
    /// claim is released when the raw-input stream reports the key-up.
    /// </summary>
    public bool TryHandleHotkeyMessage()
    {
        if (_currentPressHandled)
            return false;

        _currentPressHandled = true;
        return true;
    }

    private uint ComputePressedModifiers()
    {
        uint modifiers = 0;
        foreach (uint vk in _heldModifierVks)
            modifiers |= GetModifierFlag(vk);

        return modifiers;
    }

    private static uint GetModifierFlag(uint vk)
    {
        // RawInputInterop normalizes generic modifier codes to their
        // left/right variants, but the generic codes are accepted too in
        // case a driver or remapper delivers one that bypassed normalization.
        return vk switch
        {
            0x10 or 0xA0 or 0xA1 => ModShift,   // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
            0x11 or 0xA2 or 0xA3 => ModControl, // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
            0x12 or 0xA4 or 0xA5 => ModAlt,     // VK_MENU, VK_LMENU, VK_RMENU
            0x5B or 0x5C => ModWin,             // VK_LWIN, VK_RWIN
            _ => 0
        };
    }
}
