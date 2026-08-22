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

    [Fact]
    public void QuickAnswers_StartWithTheDocumentedDefaults()
    {
        using var directory = new TemporaryDirectory();
        var service = new AppSettingsService(directory.Path);

        Assert.Equal(string.Empty, service.BraveApiKey);
        Assert.False(service.HasBraveApiKey);
        Assert.Equal(AppSettings.DefaultQuickAnswerPrompt, service.QuickAnswerPrompt);
        Assert.Equal(AppSettings.SearchModeAuto, service.QuickAnswerSearchMode);

        // Deliberately not the spell checker's default: an answer is worth a little
        // reasoning in a way a respelling never is.
        Assert.Equal(AppSettings.DefaultQuickAnswerThinking, service.QuickAnswerThinking);
        Assert.NotEqual(service.SpellCheckThinking, service.QuickAnswerThinking);
    }

    [Fact]
    public void BraveApiKey_SurvivesAReloadAndIsNotWrittenInClearText()
    {
        using var directory = new TemporaryDirectory();
        var service = new AppSettingsService(directory.Path);

        Assert.True(service.TrySetBraveApiKey("  brave-secret-key  ", out _));

        var reloaded = new AppSettingsService(directory.Path);
        Assert.Equal("brave-secret-key", reloaded.BraveApiKey);
        Assert.True(reloaded.HasBraveApiKey);

        string json = File.ReadAllText(Path.Combine(directory.Path, "app-settings.json"));
        Assert.DoesNotContain("brave-secret-key", json);
        Assert.Contains("BraveApiKeyProtected", json);
    }

    /// <summary>The two keys are stored side by side and must not overwrite each other.</summary>
    [Fact]
    public void TheTwoApiKeysAreStoredIndependently()
    {
        using var directory = new TemporaryDirectory();
        var service = new AppSettingsService(directory.Path);

        Assert.True(service.TrySetDeepSeekApiKey("sk-deepseek", out _));
        Assert.True(service.TrySetBraveApiKey("brave-key", out _));

        var reloaded = new AppSettingsService(directory.Path);
        Assert.Equal("sk-deepseek", reloaded.DeepSeekApiKey);
        Assert.Equal("brave-key", reloaded.BraveApiKey);

        Assert.True(reloaded.TrySetBraveApiKey(string.Empty, out _));

        var cleared = new AppSettingsService(directory.Path);
        Assert.Equal("sk-deepseek", cleared.DeepSeekApiKey);
        Assert.False(cleared.HasBraveApiKey);
    }

    [Fact]
    public void UnprotectedBraveKey_IsUpgradedOnLoad()
    {
        using var directory = new TemporaryDirectory();
        string settingsFile = Path.Combine(directory.Path, "app-settings.json");
        File.WriteAllText(
            settingsFile,
            $$"""{"SchemaVersion":{{AppSettings.CurrentSchemaVersion}},"BraveApiKeyProtected":"brave-legacy-key"}""");

        var service = new AppSettingsService(directory.Path);

        Assert.Equal("brave-legacy-key", service.BraveApiKey);
        Assert.DoesNotContain("brave-legacy-key", File.ReadAllText(settingsFile));
    }

    [Theory]
    [InlineData("always", AppSettings.SearchModeAlways)]
    [InlineData("NEVER", AppSettings.SearchModeNever)]
    [InlineData("  auto ", AppSettings.SearchModeAuto)]
    [InlineData("nonsense", AppSettings.SearchModeAuto)]
    [InlineData("", AppSettings.SearchModeAuto)]
    public void QuickAnswerSearchMode_NormalizesAndSurvivesAReload(string input, string expected)
    {
        using var directory = new TemporaryDirectory();

        new AppSettingsService(directory.Path).QuickAnswerSearchMode = input;

        Assert.Equal(expected, new AppSettingsService(directory.Path).QuickAnswerSearchMode);
    }

    /// <summary>
    /// Blank falls back to this action's own default rather than the shared "off", so a
    /// cleared value does not quietly move answers onto the spell checker's setting.
    /// </summary>
    [Theory]
    [InlineData("max", "max")]
    [InlineData("DISABLED", AppSettings.ThinkingDisabled)]
    [InlineData("nonsense", AppSettings.DefaultQuickAnswerThinking)]
    [InlineData("", AppSettings.DefaultQuickAnswerThinking)]
    public void QuickAnswerThinking_NormalizesAndSurvivesAReload(string input, string expected)
    {
        using var directory = new TemporaryDirectory();

        new AppSettingsService(directory.Path).QuickAnswerThinking = input;

        Assert.Equal(expected, new AppSettingsService(directory.Path).QuickAnswerThinking);
    }

    [Fact]
    public void QuickAnswerSettings_DoNotChangeTheSpellCheckSettings()
    {
        using var directory = new TemporaryDirectory();
        var service = new AppSettingsService(directory.Path)
        {
            SpellCheckThinking = AppSettings.ThinkingDisabled,
            SpellCheckPrompt = "Only fix spelling.",
            QuickAnswerThinking = "max",
            QuickAnswerPrompt = "Answer in one word."
        };

        var reloaded = new AppSettingsService(directory.Path);
        Assert.Equal(AppSettings.ThinkingDisabled, reloaded.SpellCheckThinking);
        Assert.Equal("Only fix spelling.", reloaded.SpellCheckPrompt);
        Assert.Equal("max", reloaded.QuickAnswerThinking);
        Assert.Equal("Answer in one word.", reloaded.QuickAnswerPrompt);
    }

    [Fact]
    public void BraveApiKey_StaysOutOfProfileExports()
    {
        using var directory = new TemporaryDirectory();
        Assert.True(new AppSettingsService(directory.Path).TrySetBraveApiKey("brave-secret-key", out _));

        var profile = new DeckProfile { Name = "Export me" };
        profile.Initialize();

        string exported = ProfileService.SerializeProfileJson(profile);

        Assert.DoesNotContain("brave-secret-key", exported);
        Assert.DoesNotContain("Brave", exported, StringComparison.OrdinalIgnoreCase);
    }
}
