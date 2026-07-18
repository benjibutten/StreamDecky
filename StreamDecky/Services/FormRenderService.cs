using System.Text.RegularExpressions;
using StreamDecky.Models;

namespace StreamDecky.Services;

/// <summary>
/// Expands {token} placeholders in form templates. One shared token namespace:
/// field keys, counter names, and the built-ins below. Unknown tokens are left
/// untouched so typos stay visible instead of silently disappearing.
/// </summary>
public static partial class FormRenderService
{
    public const string DateToken = "date";
    public const string TimeToken = "time";
    public const string DateTimeToken = "datetime";
    public const string ChoiceTokenSuffix = "_choice";

    public static readonly IReadOnlyList<string> BuiltInTokens = new[] { DateToken, TimeToken, DateTimeToken };

    [GeneratedRegex(@"\{([\p{L}\p{Nd}_-]+)\}")]
    private static partial Regex TokenRegex();

    public static string GetChoiceToken(string fieldKey)
    {
        return string.IsNullOrWhiteSpace(fieldKey) ? string.Empty : fieldKey + ChoiceTokenSuffix;
    }

    /// <summary>The output template to render: the authored one, or a generated
    /// "Label: {key}" line per field when the author left it empty, so forms work
    /// without writing any tokens at all.</summary>
    public static string GetEffectiveOutputTemplate(FormTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (!string.IsNullOrWhiteSpace(template.OutputTemplate))
            return template.OutputTemplate;

        return string.Join(Environment.NewLine, template.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .Select(field => $"{field.DisplayLabel}: {{{field.Key}}}"));
    }

    /// <summary>Expands tokens using a custom resolver first (e.g. live overlay
    /// session values), falling back to counters and built-ins. Unresolved tokens
    /// stay literal.</summary>
    public static string ExpandWithResolver(
        FormTemplate template,
        string? text,
        Func<string, string?> resolve,
        DateTime? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resolve);

        return ReplaceTokens(text, token => resolve(token) ?? ResolveCounterOrBuiltIn(template, token, timestamp));
    }

    /// <summary>
    /// Expands tokens inside the field values themselves, so one field can embed
    /// another ("Payment from {who}"). Single-level substitution against the raw
    /// values of the other fields; a field's own token is left untouched to keep
    /// self-references from exploding. Counters and built-ins resolve as usual.
    /// </summary>
    public static Dictionary<string, string> ResolveFieldValues(
        FormTemplate template,
        IReadOnlyDictionary<string, string> rawValues,
        DateTime? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(rawValues);

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, raw) in rawValues)
        {
            resolved[key] = ReplaceTokens(raw, token =>
            {
                if (string.Equals(token, key, StringComparison.OrdinalIgnoreCase))
                    return null;

                if (rawValues.TryGetValue(token, out string? other))
                    return other ?? string.Empty;

                return ResolveCounterOrBuiltIn(template, token, timestamp);
            });
        }

        return resolved;
    }

    /// <summary>Renders the final output text from field values, counters, and built-ins.</summary>
    public static string Render(
        FormTemplate template,
        IReadOnlyDictionary<string, string> fieldValues,
        DateTime? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fieldValues);

        return ReplaceTokens(GetEffectiveOutputTemplate(template), token =>
        {
            if (fieldValues.TryGetValue(token, out string? value))
                return value ?? string.Empty;

            return ResolveCounterOrBuiltIn(template, token, timestamp);
        });
    }

    /// <summary>Expands counter and built-in tokens only. Used for field default
    /// values and choice option texts when an overlay session starts, so the user
    /// sees the actual number instead of the raw token.</summary>
    public static string ExpandInlineTokens(FormTemplate template, string text, DateTime? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        return ReplaceTokens(text, token => ResolveCounterOrBuiltIn(template, token, timestamp));
    }

    /// <summary>Freezes counter and built-in values for a stored submission while
    /// leaving field tokens intact so later field corrections can be re-rendered.</summary>
    public static string CreateSubmissionTemplateSnapshot(FormTemplate template, DateTime? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        var fieldKeys = template.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .SelectMany(field => field.Type == FormFieldType.Choice
                ? new[] { field.Key, GetChoiceToken(field.Key) }
                : new[] { field.Key })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ReplaceTokens(GetEffectiveOutputTemplate(template), token =>
            fieldKeys.Contains(token) ? null : ResolveCounterOrBuiltIn(template, token, timestamp));
    }

    public static string RenderTemplate(string templateText, IReadOnlyDictionary<string, string> tokenValues)
    {
        ArgumentNullException.ThrowIfNull(tokenValues);
        return ReplaceTokens(templateText, token =>
            tokenValues.TryGetValue(token, out string? value) ? value ?? string.Empty : null);
    }

    /// <summary>Tokens in the output template that match neither a field key,
    /// a counter name, nor a built-in. Surfaced as a warning in the editor.</summary>
    public static IReadOnlyList<string> GetUnknownTokens(FormTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var known = new HashSet<string>(BuiltInTokens, StringComparer.OrdinalIgnoreCase);
        foreach (var field in template.Fields)
        {
            if (!string.IsNullOrWhiteSpace(field.Key))
            {
                known.Add(field.Key);
                if (field.Type == FormFieldType.Choice)
                    known.Add(GetChoiceToken(field.Key));
            }
        }

        foreach (var counter in template.Counters)
        {
            if (!string.IsNullOrWhiteSpace(counter.Name))
                known.Add(counter.Name);
        }

        return TokenRegex().Matches(GetEffectiveOutputTemplate(template))
            .Select(match => match.Groups[1].Value)
            .Where(token => !known.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Returns authoring errors that would make field values ambiguous
    /// or lossy when rendered and stored.</summary>
    public static IReadOnlyList<string> GetValidationErrors(FormTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var errors = new List<string>();
        var tokenOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string builtIn in BuiltInTokens)
            tokenOwners[builtIn] = "a built-in token";

        void RegisterToken(string token, string owner)
        {
            if (tokenOwners.TryGetValue(token, out string? existingOwner))
                errors.Add($"Token {{{token}}} is used by both {existingOwner} and {owner}.");
            else
                tokenOwners[token] = owner;
        }

        foreach (var field in template.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                errors.Add("Every field must have a token key.");
                continue;
            }

            string owner = $"field '{field.DisplayLabel}'";
            RegisterToken(field.Key, owner);
            if (field.Type == FormFieldType.Choice)
                RegisterToken(GetChoiceToken(field.Key), $"the generated choice label for {owner}");
        }

        foreach (var counter in template.Counters)
        {
            if (string.IsNullOrWhiteSpace(counter.Name))
            {
                errors.Add("Every counter must have a token name.");
                continue;
            }

            RegisterToken(counter.Name, $"counter '{counter.Name}'");
        }

        foreach (var labelGroup in template.Fields
                     .GroupBy(field => field.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                     .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
        {
            errors.Add(string.IsNullOrWhiteSpace(labelGroup.Key)
                ? "Every field must have a label or token key."
                : $"Field label '{labelGroup.Key}' is used more than once.");
        }

        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string? ResolveCounterOrBuiltIn(FormTemplate template, string token, DateTime? timestamp)
    {
        var counter = template.Counters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, token, StringComparison.OrdinalIgnoreCase));
        if (counter != null)
            return counter.FormatValue();

        DateTime now = timestamp ?? DateTime.Now;
        if (string.Equals(token, DateToken, StringComparison.OrdinalIgnoreCase))
            return now.ToString("yyyy-MM-dd");
        if (string.Equals(token, TimeToken, StringComparison.OrdinalIgnoreCase))
            return now.ToString("HH:mm");
        if (string.Equals(token, DateTimeToken, StringComparison.OrdinalIgnoreCase))
            return now.ToString("yyyy-MM-dd HH:mm");

        return null;
    }

    private static string ReplaceTokens(string? text, Func<string, string?> resolve)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return TokenRegex().Replace(text, match => resolve(match.Groups[1].Value) ?? match.Value);
    }
}
