using System.IO;
using StreamDecky.SpeedReader;
using Xunit;

namespace StreamDecky.Tests;

public sealed class SpeedReaderSettingsStoreTests
{
    [Fact]
    public void FirstRun_StartsWithTheDefaults()
    {
        using var directory = new TemporaryDirectory();

        var settings = new SpeedReaderSettingsStore(directory.Path).Load();

        Assert.Equal(SpeedReaderSettings.DefaultWpm, settings.Wpm);
        Assert.Equal(SpeedReaderSettings.DefaultHotkeyVk, settings.HotkeyVk);
        Assert.Equal(SpeedReaderSettings.DefaultHotkeyModifiers, settings.HotkeyModifiers);
    }

    [Fact]
    public void SavedSettings_SurviveAReload()
    {
        using var directory = new TemporaryDirectory();
        var store = new SpeedReaderSettingsStore(directory.Path);

        store.Save(new SpeedReaderSettings
        {
            Wpm = 550,
            HotkeyModifiers = 0x0004,
            HotkeyVk = 0x71,
            HotkeyDisplayText = "Shift + F2",
            ScalePercent = 130,
            VerticalPositionPercent = 35
        });
        var reloaded = new SpeedReaderSettingsStore(directory.Path).Load();

        Assert.Equal(550, reloaded.Wpm);
        Assert.Equal(0x0004u, reloaded.HotkeyModifiers);
        Assert.Equal(0x71u, reloaded.HotkeyVk);
        Assert.Equal("Shift + F2", reloaded.HotkeyDisplayText);
        Assert.Equal(130, reloaded.ScalePercent);
        Assert.Equal(35, reloaded.VerticalPositionPercent);
    }

    [Theory]
    [InlineData(5, SpeedReaderSettings.MinWpm)]
    [InlineData(50_000, SpeedReaderSettings.MaxWpm)]
    public void Load_ClampsAnOutOfRangeSpeed(int stored, int expected)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "speed-reader.json"), $$"""{ "Wpm": {{stored}} }""");

        Assert.Equal(expected, new SpeedReaderSettingsStore(directory.Path).Load().Wpm);
    }

    [Fact]
    public void Load_ClampsAnOutOfRangeLayout()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "speed-reader.json"), """{ "ScalePercent": 999, "VerticalPositionPercent": -5 }""");

        var settings = new SpeedReaderSettingsStore(directory.Path).Load();

        Assert.Equal(SpeedReaderSettings.MaxScalePercent, settings.ScalePercent);
        Assert.Equal(SpeedReaderSettings.MinVerticalPositionPercent, settings.VerticalPositionPercent);
    }

    [Fact]
    public void Load_GivesAFileWithoutLayoutTheDefaultLayout()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "speed-reader.json"), """{ "Wpm": 500 }""");

        var settings = new SpeedReaderSettingsStore(directory.Path).Load();

        Assert.Equal(100, settings.ScalePercent);
        Assert.Equal(50, settings.VerticalPositionPercent);
    }

    [Fact]
    public void Load_FallsBackToDefaultsAndMovesAnUnreadableFileAside()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "speed-reader.json"), "{ not json");

        var settings = new SpeedReaderSettingsStore(directory.Path).Load();

        Assert.Equal(SpeedReaderSettings.DefaultWpm, settings.Wpm);
        Assert.False(File.Exists(Path.Combine(directory.Path, "speed-reader.json")));
        Assert.Single(Directory.GetFiles(directory.Path, "speed-reader.corrupt-*.json"));
    }
}
