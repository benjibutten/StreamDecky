using System.IO;
using StreamDecky.Models;
using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void NewInstall_StartsWithTheDocumentedDefaults()
    {
        using var directory = new TemporaryDirectory();
        var service = new AppSettingsService(directory.Path);

        Assert.Equal(string.Empty, service.DeepSeekApiKey);
        Assert.False(service.HasDeepSeekApiKey);
        Assert.Equal(AppSettings.DefaultDeepSeekModel, service.DeepSeekModel);
        Assert.Equal(AppSettings.DefaultSpellCheckPrompt, service.SpellCheckPrompt);
        Assert.Equal(AppSettings.ThinkingDisabled, service.SpellCheckThinking);
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("HIGH", "high")]
    [InlineData("  max ", "max")]
    [InlineData("nonsense", AppSettings.ThinkingDisabled)]
    [InlineData("", AppSettings.ThinkingDisabled)]
    public void SpellCheckThinking_NormalizesAndSurvivesAReload(string input, string expected)
    {
        using var directory = new TemporaryDirectory();

        new AppSettingsService(directory.Path).SpellCheckThinking = input;

        Assert.Equal(expected, new AppSettingsService(directory.Path).SpellCheckThinking);
    }

    [Fact]
    public void DeepSeekApiKey_SurvivesAReload()
    {
        using var directory = new TemporaryDirectory();

        Assert.True(new AppSettingsService(directory.Path).TrySetDeepSeekApiKey("  sk-secret-key  ", out _));

        var reloaded = new AppSettingsService(directory.Path);
        Assert.Equal("sk-secret-key", reloaded.DeepSeekApiKey);
        Assert.True(reloaded.HasDeepSeekApiKey);
    }

    [Fact]
    public void DeepSeekApiKey_IsNotWrittenInClearText()
    {
        using var directory = new TemporaryDirectory();
        var service = new AppSettingsService(directory.Path);
        Assert.True(service.TrySetDeepSeekApiKey("sk-secret-key", out _));

        string json = File.ReadAllText(Path.Combine(directory.Path, "app-settings.json"));

        Assert.DoesNotContain("sk-secret-key", json);
        Assert.Contains("DeepSeekApiKeyProtected", json);
    }

    [Fact]
    public void ModelAndPrompt_FallBackToDefaultsWhenBlanked()
    {
        using var directory = new TemporaryDirectory();
        var service = new AppSettingsService(directory.Path)
        {
            DeepSeekModel = "deepseek-v4-pro",
            SpellCheckPrompt = "Only fix spelling."
        };

        service.DeepSeekModel = "   ";
        service.SpellCheckPrompt = "  ";

        Assert.Equal(AppSettings.DefaultDeepSeekModel, service.DeepSeekModel);
        Assert.Equal(AppSettings.DefaultSpellCheckPrompt, service.SpellCheckPrompt);
    }

    [Fact]
    public void CustomModelAndPrompt_SurviveAReload()
    {
        using var directory = new TemporaryDirectory();

        var service = new AppSettingsService(directory.Path)
        {
            DeepSeekModel = "deepseek-v4-pro",
            SpellCheckPrompt = "Only fix spelling, keep Swedish."
        };

        var reloaded = new AppSettingsService(directory.Path);
        Assert.Equal("deepseek-v4-pro", reloaded.DeepSeekModel);
        Assert.Equal("Only fix spelling, keep Swedish.", reloaded.SpellCheckPrompt);
    }

    [Theory]
    [InlineData("sk-legacy-key")]                                  // hand-edited clear text
    [InlineData("plain:c2stbGVnYWN5LWtleQ==")]                     // base64 written by an older build
    public void UnprotectedApiKey_IsUpgradedOnLoad(string storedValue)
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Path, "app-settings.json");
        File.WriteAllText(settingsPath, $$"""
            {
              "SchemaVersion": 1,
              "DeepSeekApiKeyProtected": "{{storedValue}}"
            }
            """);

        // Reading the key is enough to trigger the upgrade.
        var service = new AppSettingsService(directory.Path);
        Assert.Equal("sk-legacy-key", service.DeepSeekApiKey);

        string json = File.ReadAllText(settingsPath);
        Assert.DoesNotContain("sk-legacy-key", json);
        Assert.DoesNotContain("plain:", json);
        Assert.Contains("dpapi:", json);

        // And it is still the same key after a reload.
        Assert.Equal("sk-legacy-key", new AppSettingsService(directory.Path).DeepSeekApiKey);
    }

    [Fact]
    public void ReEnteringTheSameKey_RepairsAnUnprotectedValue()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Path, "app-settings.json");

        var service = new AppSettingsService(directory.Path);
        Assert.True(service.TrySetDeepSeekApiKey("sk-legacy-key", out _));

        // Simulate a file that was hand-edited back to clear text behind the service.
        File.WriteAllText(settingsPath, """
            {
              "SchemaVersion": 1,
              "DeepSeekApiKeyProtected": "sk-legacy-key"
            }
            """);

        var reopened = new AppSettingsService(directory.Path);
        Assert.True(reopened.TrySetDeepSeekApiKey("sk-legacy-key", out string? error));
        Assert.Null(error);

        string json = File.ReadAllText(settingsPath);
        Assert.DoesNotContain("\"sk-legacy-key\"", json);
        Assert.Contains("dpapi:", json);
    }

    [Fact]
    public void TrySetDeepSeekApiKey_ReportsFailureWhenTheFileCannotBeWritten()
    {
        using var directory = new TemporaryDirectory();
        var service = new AppSettingsService(directory.Path);
        Assert.True(service.TrySetDeepSeekApiKey("sk-first-key", out _));

        // A directory where the settings file belongs makes every write fail.
        string settingsPath = Path.Combine(directory.Path, "app-settings.json");
        File.Delete(settingsPath);
        Directory.CreateDirectory(settingsPath);

        try
        {
            var blocked = new AppSettingsService(directory.Path);

            Assert.False(blocked.TrySetDeepSeekApiKey("sk-second-key", out string? error));
            Assert.False(string.IsNullOrWhiteSpace(error));

            // The in-memory value is rolled back, so the getter never claims a key that
            // is not on disk.
            Assert.Equal(string.Empty, blocked.DeepSeekApiKey);
        }
        finally
        {
            Directory.Delete(settingsPath, recursive: true);
        }
    }

    [Fact]
    public void ApiKey_StaysOutOfProfileExports()
    {
        using var directory = new TemporaryDirectory();
        Assert.True(new AppSettingsService(directory.Path).TrySetDeepSeekApiKey("sk-secret-key", out _));

        var profile = new DeckProfile { Name = "Export me" };
        profile.Initialize();

        string exported = ProfileService.SerializeProfileJson(profile);

        Assert.DoesNotContain("sk-secret-key", exported);
        Assert.DoesNotContain("DeepSeek", exported, StringComparison.OrdinalIgnoreCase);
    }
}
