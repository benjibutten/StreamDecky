using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class ForegroundIntegrityMonitorTests
{
    private const int Medium = 0x2000;
    private const int High = 0x3000;

    [Fact]
    public void CheckForeground_WhenForegroundIsHigherIntegrity_Warns()
    {
        int raised = 0;
        var monitor = new ForegroundIntegrityMonitor(
            getForegroundProcessId: () => 1234,
            getProcessIntegrityLevel: _ => High,
            getOwnIntegrityLevel: () => Medium,
            consecutiveChecksRequired: 1);
        monitor.HigherIntegrityForegroundDetected += () => raised++;

        Assert.True(monitor.CheckForeground());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void CheckForeground_WhenForegroundIsSameOrLowerIntegrity_DoesNothing()
    {
        int raised = 0;
        var monitor = new ForegroundIntegrityMonitor(
            getForegroundProcessId: () => 1234,
            getProcessIntegrityLevel: _ => Medium,
            getOwnIntegrityLevel: () => Medium,
            consecutiveChecksRequired: 1);
        monitor.HigherIntegrityForegroundDetected += () => raised++;

        Assert.False(monitor.CheckForeground());
        Assert.Equal(0, raised);
    }

    [Fact]
    public void CheckForeground_WarnsOnlyOncePerProcess()
    {
        int raised = 0;
        var monitor = new ForegroundIntegrityMonitor(
            getForegroundProcessId: () => 1234,
            getProcessIntegrityLevel: _ => High,
            getOwnIntegrityLevel: () => Medium,
            consecutiveChecksRequired: 1);
        monitor.HigherIntegrityForegroundDetected += () => raised++;

        Assert.True(monitor.CheckForeground());
        Assert.False(monitor.CheckForeground());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void CheckForeground_WhenForegroundIsSelf_DoesNothing()
    {
        var monitor = new ForegroundIntegrityMonitor(
            getForegroundProcessId: () => (uint)Environment.ProcessId,
            getProcessIntegrityLevel: _ => High,
            getOwnIntegrityLevel: () => Medium,
            consecutiveChecksRequired: 1);

        Assert.False(monitor.CheckForeground());
    }

    [Fact]
    public void CheckForeground_WhenOwnIntegrityUnknown_IsInactiveAndDoesNothing()
    {
        var monitor = new ForegroundIntegrityMonitor(
            getForegroundProcessId: () => 1234,
            getProcessIntegrityLevel: _ => High,
            getOwnIntegrityLevel: () => null,
            consecutiveChecksRequired: 1);

        Assert.False(monitor.IsActive);
        Assert.False(monitor.CheckForeground());
    }

    [Fact]
    public void CheckForeground_WarnsOnlyAfterElevatedWindowStaysInForeground()
    {
        int raised = 0;
        var monitor = new ForegroundIntegrityMonitor(
            getForegroundProcessId: () => 1234,
            getProcessIntegrityLevel: _ => High,
            getOwnIntegrityLevel: () => Medium,
            consecutiveChecksRequired: 3);
        monitor.HigherIntegrityForegroundDetected += () => raised++;

        Assert.False(monitor.CheckForeground());
        Assert.False(monitor.CheckForeground());
        Assert.True(monitor.CheckForeground());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void CheckForeground_WhenElevatedWindowIsTransient_DoesNotWarn()
    {
        int raised = 0;
        uint foreground = 1234;
        var monitor = new ForegroundIntegrityMonitor(
            getForegroundProcessId: () => foreground,
            // Only the transient elevated tool (pid 1234) is elevated; the normal window (5678) is not.
            getProcessIntegrityLevel: pid => pid == 1234 ? High : Medium,
            getOwnIntegrityLevel: () => Medium,
            consecutiveChecksRequired: 3);
        monitor.HigherIntegrityForegroundDetected += () => raised++;

        // Elevated tool flashes in front for one check, then focus returns to a normal window.
        Assert.False(monitor.CheckForeground());
        foreground = 5678;
        Assert.False(monitor.CheckForeground());
        Assert.False(monitor.CheckForeground());

        // The elevated tool comes back briefly: the streak restarted, so still no warning.
        foreground = 1234;
        Assert.False(monitor.CheckForeground());
        Assert.Equal(0, raised);
    }
}
