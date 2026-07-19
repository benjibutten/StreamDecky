using System.Runtime.InteropServices;

namespace StreamDecky.Helpers;

internal static class XInputInterop
{
    internal const ushort GamepadDPadUp = 0x0001;
    internal const ushort GamepadDPadDown = 0x0002;
    internal const ushort GamepadDPadLeft = 0x0004;
    internal const ushort GamepadDPadRight = 0x0008;
    internal const ushort GamepadStart = 0x0010;
    internal const ushort GamepadBack = 0x0020;
    internal const ushort GamepadLeftThumb = 0x0040;
    internal const ushort GamepadRightThumb = 0x0080;
    internal const ushort GamepadLeftShoulder = 0x0100;
    internal const ushort GamepadRightShoulder = 0x0200;
    internal const ushort GamepadA = 0x1000;
    internal const ushort GamepadB = 0x2000;
    internal const ushort GamepadX = 0x4000;
    internal const ushort GamepadY = 0x8000;

    private const uint ErrorSuccess = 0;
    private const uint ErrorDeviceNotConnected = 1167;

    private enum XInputBackend
    {
        Unknown,
        XInput14,
        XInput13,
        XInput910,
        None
    }

    private static XInputBackend _backend = XInputBackend.Unknown;

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetState14(uint dwUserIndex, out XInputState pState);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetState13(uint dwUserIndex, out XInputState pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern uint XInputGetState910(uint dwUserIndex, out XInputState pState);

    internal static bool TryGetFirstConnectedState(out XInputState state)
    {
        for (uint i = 0; i < 4; i++)
        {
            if (TryGetState(i, out state))
                return true;
        }

        state = default;
        return false;
    }

    internal static bool TryGetState(uint userIndex, out XInputState state)
    {
        uint result = GetState(userIndex, out state);
        return result == ErrorSuccess;
    }

    internal static bool IsButtonPressed(ushort buttons, ushort button)
    {
        return (buttons & button) == button;
    }

    internal static bool AreButtonsPressed(ushort buttons, ushort requiredButtons)
    {
        return (buttons & requiredButtons) == requiredButtons;
    }

    private static uint GetState(uint userIndex, out XInputState state)
    {
        if (_backend != XInputBackend.Unknown)
            return InvokeBackend(_backend, userIndex, out state);

        if (TryProbeBackend(XInputBackend.XInput14, userIndex, out state, out uint result14))
        {
            _backend = XInputBackend.XInput14;
            return result14;
        }

        if (TryProbeBackend(XInputBackend.XInput13, userIndex, out state, out uint result13))
        {
            _backend = XInputBackend.XInput13;
            return result13;
        }

        if (TryProbeBackend(XInputBackend.XInput910, userIndex, out state, out uint result910))
        {
            _backend = XInputBackend.XInput910;
            return result910;
        }

        _backend = XInputBackend.None;
        state = default;
        return ErrorDeviceNotConnected;
    }

    private static bool TryProbeBackend(
        XInputBackend backend,
        uint userIndex,
        out XInputState state,
        out uint result)
    {
        try
        {
            result = InvokeBackend(backend, userIndex, out state);
            return true;
        }
        catch (DllNotFoundException)
        {
            state = default;
            result = ErrorDeviceNotConnected;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            state = default;
            result = ErrorDeviceNotConnected;
            return false;
        }
    }

    private static uint InvokeBackend(XInputBackend backend, uint userIndex, out XInputState state)
    {
        switch (backend)
        {
            case XInputBackend.XInput14:
                return XInputGetState14(userIndex, out state);
            case XInputBackend.XInput13:
                return XInputGetState13(userIndex, out state);
            case XInputBackend.XInput910:
                return XInputGetState910(userIndex, out state);
            default:
                state = default;
                return ErrorDeviceNotConnected;
        }
    }
}