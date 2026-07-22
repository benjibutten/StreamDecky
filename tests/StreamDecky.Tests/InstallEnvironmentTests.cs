using StreamDecky.Updates;
using Xunit;

namespace StreamDecky.Tests;

public sealed class InstallEnvironmentTests
{
    [Theory]
    [InlineData(@"C:\Users\alice\AppData\Local\Microsoft\WinGet\Packages\BenjiButten.StreamDecky_Microsoft.Winget.Source_8wekyb3d8bbwe\StreamDecky.exe")]
    [InlineData(@"C:\Users\alice\AppData\Local\Microsoft\WinGet\Links\streamdecky.exe")]
    [InlineData(@"C:\Program Files\WinGet\Packages\BenjiButten.StreamDecky\StreamDecky.exe")]
    public void IsWingetPath_DetectsWingetInstallLocations(string executablePath)
    {
        Assert.True(InstallEnvironment.IsWingetPath(executablePath));
    }

    [Theory]
    [InlineData(@"C:\Users\alice\AppData\Local\Programs\StreamDecky\StreamDecky.exe")]
    [InlineData(@"C:\Program Files\StreamDecky\StreamDecky.exe")]
    [InlineData(@"D:\Games\StreamDecky\StreamDecky.exe")]
    public void IsWingetPath_IgnoresOtherInstallLocations(string executablePath)
    {
        Assert.False(InstallEnvironment.IsWingetPath(executablePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsWingetPath_HandlesMissingPath(string? executablePath)
    {
        Assert.False(InstallEnvironment.IsWingetPath(executablePath));
    }

    [Theory]
    // A custom portablePackageUserRoot / portablePackageMachineRoot moves the
    // install off the default WinGet roots; the recorded InstallLocation still
    // covers the running executable.
    [InlineData(@"D:\PortableApps\BenjiButten.StreamDecky_Microsoft.Winget.Source_8wekyb3d8bbwe", @"D:\PortableApps\BenjiButten.StreamDecky_Microsoft.Winget.Source_8wekyb3d8bbwe\StreamDecky.exe")]
    [InlineData(@"D:\PortableApps\StreamDecky\", @"D:\PortableApps\StreamDecky\StreamDecky.exe")]
    [InlineData(@"E:\apps\sd", @"E:\apps\sd\sub\StreamDecky.exe")]
    public void IsExecutableWithin_DetectsExecutableInsideInstallLocation(string installLocation, string executablePath)
    {
        Assert.True(InstallEnvironment.IsExecutableWithin(executablePath, installLocation));
    }

    [Theory]
    // A sibling directory that merely shares a name prefix must not match.
    [InlineData(@"D:\PortableApps\StreamDecky", @"D:\PortableApps\StreamDeckyBackup\StreamDecky.exe")]
    [InlineData(@"D:\PortableApps\StreamDecky", @"C:\Program Files\StreamDecky\StreamDecky.exe")]
    public void IsExecutableWithin_RejectsExecutableOutsideInstallLocation(string installLocation, string executablePath)
    {
        Assert.False(InstallEnvironment.IsExecutableWithin(executablePath, installLocation));
    }

    [Theory]
    [InlineData(null, @"D:\PortableApps\StreamDecky\StreamDecky.exe")]
    [InlineData("", @"D:\PortableApps\StreamDecky\StreamDecky.exe")]
    [InlineData(@"D:\PortableApps\StreamDecky", null)]
    [InlineData(@"D:\PortableApps\StreamDecky", "")]
    public void IsExecutableWithin_HandlesMissingInput(string? installLocation, string? executablePath)
    {
        Assert.False(InstallEnvironment.IsExecutableWithin(executablePath, installLocation));
    }
}
