using StreamDecky.Admin;

namespace StreamDecky.Services;

/// <summary>
/// Keeps synthetic keyboard and clipboard actions ordered. Overlapping actions
/// can otherwise interleave modifier states or replace each other's clipboard
/// text before Ctrl+V is sent.
/// </summary>
internal static class InputActionGate
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    /// <summary>
    /// Raised on a background thread after an action sent its keys to a program running
    /// as administrator while StreamDecky is not, with that program's name and process id.
    /// Windows drops those keys.
    /// </summary>
    public static event Action<string, int>? KeysSentToElevatedProgram;

    public static async Task RunAsync(Func<Task> operation)
    {
        await Semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
            ReportElevatedTarget();
        }
        finally
        {
            Semaphore.Release();
        }
    }

    // Checked once the keys are out: the actions wait for focus to return to the
    // target window before sending, so only then is the foreground the program that got them.
    private static void ReportElevatedTarget()
    {
        if (Elevation.IsElevated || !Elevation.CanRunAsAdministrator)
            return;

        if (ElevatedForeground.Describe() is { } target)
            KeysSentToElevatedProgram?.Invoke(target.Name, target.ProcessId);
    }
}
