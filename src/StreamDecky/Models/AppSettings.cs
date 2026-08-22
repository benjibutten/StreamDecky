namespace StreamDecky.Models;

/// <summary>
/// Machine-wide settings that must not travel with a profile export, currently the
/// DeepSeek credentials and prompt behind the overlay's text helper widget.
/// </summary>
public class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public const string DefaultDeepSeekModel = "deepseek-v4-flash";

    /// <summary>
    /// DeepSeek turns thinking on by default at <c>high</c> effort. Respelling a chat
    /// line needs none of it, so the widget asks for the non-thinking mode and only
    /// spends tokens on reasoning if the user opts back in.
    /// </summary>
    public const string ThinkingDisabled = "disabled";

    public static readonly IReadOnlyList<string> ThinkingLevels = new[]
    {
        ThinkingDisabled,
        "low",
        "high",
        "max"
    };

    public const string DefaultSpellCheckPrompt =
        """
        You correct spelling for a person with dyslexia. You get the raw text the user typed and return the same text with the spelling fixed.

        Rules:
        - Keep the original language. Never translate.
        - Sound out badly spelled words and use the surrounding sentence to work out which word was meant, then write that word correctly.
        - Keep the meaning, tone, word order, slang and abbreviations. Do not rewrite the style, do not add or remove information, and never answer, continue or comment on the text.
        - Fix casing, doubled letters and missing or extra spaces. Only add punctuation where a sentence clearly lost it.
        - Leave line breaks, emoji, @mentions, #tags, URLs, numbers and any /commands exactly as they are.
        - If a word is unreadable even in context, leave it untouched rather than guessing wildly.
        - If the text is already correct, return it unchanged.

        Reply with the corrected text only: no quotes, no code block, no explanation.
        """;

    public int SchemaVersion { get; set; }

    /// <summary>DPAPI-protected DeepSeek API key. See <see cref="Helpers.DataProtection"/>.</summary>
    public string DeepSeekApiKeyProtected { get; set; } = string.Empty;

    public string DeepSeekModel { get; set; } = DefaultDeepSeekModel;

    public string SpellCheckPrompt { get; set; } = DefaultSpellCheckPrompt;

    /// <summary>One of <see cref="ThinkingLevels"/>; <see cref="ThinkingDisabled"/> is the fast default.</summary>
    public string SpellCheckThinking { get; set; } = ThinkingDisabled;

    public void Initialize()
    {
        if (SchemaVersion <= 0)
            SchemaVersion = CurrentSchemaVersion;

        DeepSeekApiKeyProtected ??= string.Empty;

        if (string.IsNullOrWhiteSpace(DeepSeekModel))
            DeepSeekModel = DefaultDeepSeekModel;

        if (string.IsNullOrWhiteSpace(SpellCheckPrompt))
            SpellCheckPrompt = DefaultSpellCheckPrompt;

        SpellCheckThinking = NormalizeThinking(SpellCheckThinking);
    }

    public static string NormalizeThinking(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ThinkingDisabled;

        string trimmed = value.Trim().ToLowerInvariant();
        return ThinkingLevels.Contains(trimmed) ? trimmed : ThinkingDisabled;
    }
}
