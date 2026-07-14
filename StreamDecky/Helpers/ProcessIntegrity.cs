using System.Runtime.InteropServices;

namespace StreamDecky.Helpers;

/// <summary>
/// Reads Windows mandatory integrity levels for processes. A global hotkey registered by a
/// medium-integrity process is silently swallowed by UIPI while a higher-integrity (elevated)
/// window owns the foreground, which is why the overlay hotkey can stop working inside a game
/// launched as administrator. The returned value is the integrity SID RID
/// (e.g. 0x2000 = Medium, 0x3000 = High, 0x4000 = System); larger means more privileged.
/// </summary>
public static class ProcessIntegrity
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint nSubAuthority);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25;

    /// <summary>Returns the process id that owns the current foreground window, or 0 if unavailable.</summary>
    public static uint GetForegroundProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return 0;

        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId;
    }

    /// <summary>Returns the integrity level of the given process, or null if it could not be read.</summary>
    public static int? GetProcessIntegrityLevel(uint processId)
    {
        // PROCESS_QUERY_LIMITED_INFORMATION is granted across integrity levels, so this works
        // even when the foreground process is more privileged than we are.
        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero)
            return null;

        try
        {
            return ReadIntegrityLevel(hProcess);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    /// <summary>Returns the integrity level of the current process, or null if it could not be read.</summary>
    public static int? GetCurrentProcessIntegrityLevel()
    {
        return ReadIntegrityLevel(GetCurrentProcess());
    }

    private static int? ReadIntegrityLevel(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, TOKEN_QUERY, out IntPtr token))
            return null;

        try
        {
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int length);
            if (length <= 0)
                return null;

            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                    return null;

                // TOKEN_MANDATORY_LABEL begins with a SID_AND_ATTRIBUTES whose first field is the SID
                // pointer. The integrity level is the SID's last sub-authority (RID).
                IntPtr pSid = Marshal.ReadIntPtr(buffer);
                int subAuthorityCount = Marshal.ReadByte(GetSidSubAuthorityCount(pSid));
                if (subAuthorityCount == 0)
                    return null;

                IntPtr pRid = GetSidSubAuthority(pSid, (uint)(subAuthorityCount - 1));
                return Marshal.ReadInt32(pRid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
