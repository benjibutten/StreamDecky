using System.Runtime.InteropServices;
using System.Windows.Interop;
using StreamDecky.Helpers;
using Xunit;

namespace StreamDecky.Tests;

public sealed class OverlayInteropTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [Fact]
    public void BelongsToThisProcess_DistinguishesOwnWindowsFromForeignOnes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var source = new HwndSource(new HwndSourceParameters("OverlayInteropTests"));
                try
                {
                    Assert.True(OverlayInterop.BelongsToThisProcess(source.Handle));
                    Assert.False(OverlayInterop.BelongsToThisProcess(GetDesktopWindow()));
                    Assert.False(OverlayInterop.BelongsToThisProcess(IntPtr.Zero));
                }
                finally
                {
                    source.Dispose();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The interop test timed out.");
        if (failure != null)
            throw failure;
    }
}
