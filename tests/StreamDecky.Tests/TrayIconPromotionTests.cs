using StreamDecky.Helpers;
using Xunit;

namespace StreamDecky.Tests;

public sealed class TrayIconPromotionTests
{
    private const string InstalledPath = @"C:\Program Files\StreamDecky\StreamDecky.exe";

    [Theory]
    [InlineData(@"C:\Program Files\StreamDecky\StreamDecky.exe")]
    [InlineData(@"c:\program files\streamdecky\STREAMDECKY.EXE")]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\StreamDecky\StreamDecky.exe")]
    public void IsSameExecutable_MatchesPathsAsWindowsRecordsThem(string recordedPath)
    {
        Assert.True(TrayIconPromotion.IsSameExecutable(recordedPath, InstalledPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Tools\StreamDecky.exe")]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Other\StreamDecky.exe")]
    public void IsSameExecutable_RejectsOtherPaths(string? recordedPath)
    {
        Assert.False(TrayIconPromotion.IsSameExecutable(recordedPath, InstalledPath));
    }
}
