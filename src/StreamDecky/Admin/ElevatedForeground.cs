using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StreamDecky.Admin;

/// <summary>
/// Tells whether the window that has focus belongs to a program running as
/// administrator. Windows drops the keys a StreamDecky without administrator
/// rights sends to such a program, and <c>SendInput</c> does not report it.
/// </summary>
internal static class ElevatedForeground
{
    /// <summary>
    /// Returns a readable name for the foreground program and its process id when it
    /// runs as administrator, or null when it does not or cannot be read.
    /// </summary>
    public static (string Name, int ProcessId)? Describe()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out int processId);
        if (processId == 0 || processId == Environment.ProcessId)
            return null;

        using SafeProcessHandle process = Elevation.OpenForQuery(processId);
        if (process.IsInvalid || !Elevation.IsProcessElevated(process))
            return null;

        return (DescribeProcess(process), processId);
    }

    // "Task Manager" reads better than "Taskmgr"; the file description is what
    // Windows itself shows for the program.
    private static string DescribeProcess(SafeProcessHandle process)
    {
        var path = new StringBuilder(1024);
        int length = path.Capacity;
        if (!QueryFullProcessImageName(process, 0, path, ref length))
            return "A program";

        string imagePath = path.ToString();
        try
        {
            if (FileVersionInfo.GetVersionInfo(imagePath).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (FileNotFoundException)
        {
            // The name from the path below still says which program it is.
        }

        return Path.GetFileNameWithoutExtension(imagePath);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder exeName, ref int size);
}
