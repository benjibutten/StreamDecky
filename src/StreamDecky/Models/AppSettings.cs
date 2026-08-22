namespace StreamDecky.Models;

/// <summary>
/// Machine-wide settings that must not travel with a profile export: the DeepSeek and
/// Brave credentials and the prompts behind the overlay's text helper widget.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Bumped to 2 when quick answers added the Brave key and its own prompt. An older
    /// build refuses to save a file it does not understand rather than rewriting it
    /// without those fields, which would silently drop the user's Brave key.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

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

    /// <summary>The model decides per question whether to search. See <see cref="SearchModes"/>.</summary>
    public const string SearchModeAuto = "auto";

    /// <summary>Every question is searched, skipping the routing call entirely.</summary>
    public const string SearchModeAlways = "always";

    /// <summary>Never search; answer from the model alone and label it as unchecked.</summary>
    public const string SearchModeNever = "never";

    public static readonly IReadOnlyList<string> SearchModes = new[]
    {
        SearchModeAuto,
        SearchModeAlways,
        SearchModeNever
    };

    /// <summary>
    /// Unlike spell checking, a quick answer is a factual claim the user may read out on
    /// stream, and a little reasoning measurably improves how well the model sticks to
    /// the sources and admits when they do not answer the question. Low is the balance
    /// point: noticeably steadier than off, without the seconds that high costs.
    /// </summary>
    public const string DefaultQuickAnswerThinking = "low";

    public const string DefaultQuickAnswerPrompt =
        """
        You answer one short question for someone who is live on stream and needs the answer in seconds.

        Rules:
        - Answer in the same language as the question.
        - Put the answer itself in the first sentence. Add at most two more sentences, and only when they genuinely matter.
        - When search results are provided, base the answer only on what they say. Do not fill gaps from your own memory.
        - When search results are provided but none of them actually answer the question, say exactly that instead of guessing.
        - When search results disagree, say so in a few words and lead with the most recent one.
        - When no search results are provided, answer from your own knowledge, and say so plainly if the answer is the kind that could have changed since.
        - Give dates, numbers and names exactly as the source states them. Never estimate a figure a source states.
        - No preamble, no sign-off, no headings, no citation markers. Plain sentences only.
        """;

    public int SchemaVersion { get; set; }

    /// <summary>DPAPI-protected DeepSeek API key. See <see cref="Helpers.DataProtection"/>.</summary>
    public string DeepSeekApiKeyProtected { get; set; } = string.Empty;

    public string DeepSeekModel { get; set; } = DefaultDeepSeekModel;

    public string SpellCheckPrompt { get; set; } = DefaultSpellCheckPrompt;

    /// <summary>One of <see cref="ThinkingLevels"/>; <see cref="ThinkingDisabled"/> is the fast default.</summary>
    public string SpellCheckThinking { get; set; } = ThinkingDisabled;

    /// <summary>DPAPI-protected Brave Search API key. See <see cref="Helpers.DataProtection"/>.</summary>
    public string BraveApiKeyProtected { get; set; } = string.Empty;

    public string QuickAnswerPrompt { get; set; } = DefaultQuickAnswerPrompt;

    /// <summary>
    /// Separate from <see cref="SpellCheckThinking"/> on purpose: the two actions share a
    /// key and a model but want opposite settings, and one slider for both would force a
    /// bad trade on whichever the user cares about less.
    /// </summary>
    public string QuickAnswerThinking { get; set; } = DefaultQuickAnswerThinking;

    /// <summary>One of <see cref="SearchModes"/>.</summary>
    public string QuickAnswerSearchMode { get; set; } = SearchModeAuto;

    public void Initialize()
    {
        if (SchemaVersion <= 0)
            SchemaVersion = CurrentSchemaVersion;

        DeepSeekApiKeyProtected ??= string.Empty;
        BraveApiKeyProtected ??= string.Empty;

        if (string.IsNullOrWhiteSpace(DeepSeekModel))
            DeepSeekModel = DefaultDeepSeekModel;

        if (string.IsNullOrWhiteSpace(SpellCheckPrompt))
            SpellCheckPrompt = DefaultSpellCheckPrompt;

        if (string.IsNullOrWhiteSpace(QuickAnswerPrompt))
            QuickAnswerPrompt = DefaultQuickAnswerPrompt;

        SpellCheckThinking = NormalizeThinking(SpellCheckThinking);
        QuickAnswerThinking = NormalizeThinking(QuickAnswerThinking, DefaultQuickAnswerThinking);
        QuickAnswerSearchMode = NormalizeSearchMode(QuickAnswerSearchMode);
    }

    public static string NormalizeThinking(string? value) => NormalizeThinking(value, ThinkingDisabled);

    /// <summary>
    /// Falls back to <paramref name="fallback"/> for a missing or unrecognised level, so
    /// each action can keep its own default while sharing the same set of levels.
    /// </summary>
    public static string NormalizeThinking(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string trimmed = value.Trim().ToLowerInvariant();
        return ThinkingLevels.Contains(trimmed) ? trimmed : fallback;
    }

    public static string NormalizeSearchMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SearchModeAuto;

        string trimmed = value.Trim().ToLowerInvariant();
        return SearchModes.Contains(trimmed) ? trimmed : SearchModeAuto;
    }
}
