namespace StreamDecky.Services;

/// <summary>
/// Keeps synthetic keyboard and clipboard actions ordered. Overlapping actions
/// can otherwise interleave modifier states or replace each other's clipboard
/// text before Ctrl+V is sent.
/// </summary>
internal static class InputActionGate
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    public static async Task RunAsync(Func<Task> operation)
    {
        await Semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            Semaphore.Release();
        }
    }
}
