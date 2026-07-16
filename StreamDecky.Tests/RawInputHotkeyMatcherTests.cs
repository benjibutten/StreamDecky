using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class RawInputHotkeyMatcherTests
{
    private const uint ModControl = 0x0002;
    private const uint VkGenericControl = 0x11;
    private const uint VkLeftShift = 0xA0;
    private const uint VkLeftControl = 0xA2;
    private const uint VkRightControl = 0xA3;
    private const uint VkF12 = 0x7B;

    [Fact]
    public void ProcessKeyEvent_FreshPressWithTrackedModifier_Triggers()
    {
        var matcher = CreateMatcher();

        Assert.False(matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: true));
        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_ModifierReleasedBeforeKeyDown_DoesNotTrigger()
    {
        var matcher = CreateMatcher();

        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: true);
        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: false);

        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_ExtraModifierHeld_DoesNotTrigger()
    {
        var matcher = CreateMatcher();

        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: true);
        matcher.ProcessKeyEvent(VkLeftShift, isKeyDown: true);

        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_BothControlKeysHeldReleaseOne_ModifierStaysPressed()
    {
        var matcher = CreateMatcher();

        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: true);
        matcher.ProcessKeyEvent(VkRightControl, isKeyDown: true);
        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: false);

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_BothControlKeysReleased_ModifierClears()
    {
        var matcher = CreateMatcher();

        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: true);
        matcher.ProcessKeyEvent(VkRightControl, isKeyDown: true);
        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: false);
        matcher.ProcessKeyEvent(VkRightControl, isKeyDown: false);

        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_GenericModifierCode_StillTracksGroup()
    {
        var matcher = CreateMatcher();

        matcher.ProcessKeyEvent(VkGenericControl, isKeyDown: true);

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void Configure_SeededModifierKeys_TriggerWithoutModifierEvents()
    {
        var matcher = new RawInputHotkeyMatcher();
        matcher.Configure(ModControl, VkF12, new[] { VkRightControl });

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_AutoRepeat_DoesNotRetrigger()
    {
        var matcher = CreateMatcherWithControlHeld();

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_AfterRelease_TriggersAgain()
    {
        var matcher = CreateMatcherWithControlHeld();

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: false));
        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_AddingModifierDuringAutoRepeat_DoesNotTrigger()
    {
        var matcher = CreateMatcher();

        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: true);
        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_OtherKey_DoesNotTrigger()
    {
        var matcher = CreateMatcherWithControlHeld();

        Assert.False(matcher.ProcessKeyEvent(0x41, isKeyDown: true));
    }

    [Fact]
    public void ProcessKeyEvent_UnconfiguredMatcher_DoesNotTrigger()
    {
        var matcher = new RawInputHotkeyMatcher();

        Assert.False(matcher.ProcessKeyEvent(0, isKeyDown: true));
    }

    [Fact]
    public void TryHandleHotkeyMessage_AfterRawInputTriggered_IsRejected()
    {
        var matcher = CreateMatcherWithControlHeld();

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));

        Assert.False(matcher.TryHandleHotkeyMessage());
    }

    [Fact]
    public void ProcessKeyEvent_AfterHotkeyMessageHandled_DoesNotTriggerForSamePress()
    {
        var matcher = CreateMatcherWithControlHeld();

        Assert.True(matcher.TryHandleHotkeyMessage());

        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void TryHandleHotkeyMessage_AfterKeyRelease_HandlesNextPress()
    {
        var matcher = CreateMatcherWithControlHeld();

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
        matcher.ProcessKeyEvent(VkF12, isKeyDown: false);

        Assert.True(matcher.TryHandleHotkeyMessage());
    }

    [Fact]
    public void ProcessKeyEvent_TwoSeparatePresses_TriggerTwice()
    {
        var matcher = CreateMatcherWithControlHeld();

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
        matcher.ProcessKeyEvent(VkF12, isKeyDown: false);
        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void Configure_WhileHotkeyKeyAlreadyHeld_AutoRepeatDoesNotTrigger()
    {
        var matcher = new RawInputHotkeyMatcher();

        // The user is still holding Ctrl+F12 from recording the hotkey; the
        // next queued key-down / auto-repeat must not toggle the overlay.
        matcher.Configure(ModControl, VkF12, new[] { VkLeftControl }, hotkeyKeyAlreadyHeld: true);

        Assert.False(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void Configure_WhileHotkeyKeyAlreadyHeld_TriggersAfterReleaseAndFreshPress()
    {
        var matcher = new RawInputHotkeyMatcher();
        matcher.Configure(ModControl, VkF12, new[] { VkLeftControl }, hotkeyKeyAlreadyHeld: true);

        matcher.ProcessKeyEvent(VkF12, isKeyDown: false);

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    [Fact]
    public void Configure_WhileHotkeyKeyNotHeld_FreshPressTriggers()
    {
        var matcher = CreateMatcherWithControlHeld();
        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
        matcher.ProcessKeyEvent(VkF12, isKeyDown: false);

        matcher.Configure(ModControl, VkF12, new[] { VkLeftControl });

        Assert.True(matcher.ProcessKeyEvent(VkF12, isKeyDown: true));
    }

    private static RawInputHotkeyMatcher CreateMatcher()
    {
        var matcher = new RawInputHotkeyMatcher();
        matcher.Configure(ModControl, VkF12);
        return matcher;
    }

    private static RawInputHotkeyMatcher CreateMatcherWithControlHeld()
    {
        var matcher = CreateMatcher();
        matcher.ProcessKeyEvent(VkLeftControl, isKeyDown: true);
        return matcher;
    }
}
