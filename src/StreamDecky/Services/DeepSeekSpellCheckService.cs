using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StreamDecky.Helpers;
using StreamDecky.Models;

namespace StreamDecky.Services;

/// <summary>Outcome of one spell-check round trip. <see cref="Text"/> is only meaningful when <see cref="Success"/>.</summary>
public sealed record SpellCheckResult(bool Success, string Text, string? ErrorMessage)
{
    public static SpellCheckResult Ok(string text) => new(true, text, null);

    public static SpellCheckResult Fail(string message) => new(false, string.Empty, message);
}

/// <summary>
/// Sends the overlay text helper's content to DeepSeek's OpenAI-compatible chat
/// completions endpoint and returns the respelled text.
/// </summary>
public class DeepSeekSpellCheckService
{
    public const string DefaultEndpoint = "https://api.deepseek.com/chat/completions";

    /// <summary>Guards against pasting a whole document into a widget meant for a chat line.</summary>
    public const int MaxInputLength = 4000;

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    public DeepSeekSpellCheckService(HttpClient? httpClient = null, string? endpoint = null)
    {
        // Only the client we own gets its timeout adjusted; HttpClient forbids
        // changing that once an injected instance has sent a request.
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint;
    }

    public async Task<SpellCheckResult> CorrectAsync(
        string text,
        string apiKey,
        string? model = null,
        string? prompt = null,
        string? thinking = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SpellCheckResult.Fail("There is no text to correct yet.");

        if (string.IsNullOrWhiteSpace(apiKey))
            return SpellCheckResult.Fail("Add your DeepSeek API key in Settings first.");

        if (text.Length > MaxInputLength)
            return SpellCheckResult.Fail($"The text is too long to correct ({text.Length} of max {MaxInputLength} characters).");

        string requestJson = BuildRequestJson(
            string.IsNullOrWhiteSpace(model) ? AppSettings.DefaultDeepSeekModel : model.Trim(),
            string.IsNullOrWhiteSpace(prompt) ? AppSettings.DefaultSpellCheckPrompt : prompt,
            text,
            AppSettings.NormalizeThinking(thinking));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return SpellCheckResult.Fail(DescribeFailure(response.StatusCode, responseBody));

            string? incompleteReason = GetIncompleteReason(responseBody);
            if (incompleteReason != null)
                return SpellCheckResult.Fail(DescribeIncompleteAnswer(incompleteReason));

            string? corrected = ExtractContent(responseBody);
            if (string.IsNullOrWhiteSpace(corrected))
                return SpellCheckResult.Fail("DeepSeek returned an empty answer. Try again.");

            // Emptiness is checked after cleaning too: an answer that is nothing but a
            // quote pair or an empty code fence survives the check above but cleans away
            // to nothing, and must not wipe the user's text.
            string cleaned = Clean(corrected);
            if (string.IsNullOrWhiteSpace(cleaned))
                return SpellCheckResult.Fail("DeepSeek returned an empty answer. Try again.");

            return SpellCheckResult.Ok(RestoreOuterWhitespace(text, cleaned));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SpellCheckResult.Fail("Spell check was cancelled.");
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces its own timeout as a cancellation with no token set.
            return SpellCheckResult.Fail("DeepSeek did not answer in time. Try again.");
        }
        catch (HttpRequestException ex)
        {
            AppDiagnostics.Warning("DeepSeek spell check request failed.", ex);
            return SpellCheckResult.Fail("Could not reach DeepSeek. Check your internet connection.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("DeepSeek spell check failed unexpectedly.", ex);
            return SpellCheckResult.Fail("Spell check failed. See the log for details.");
        }
    }

    /// <summary>
    /// A hard ceiling on what one correction may cost. A respelling is about as long as
    /// its input, so the cap tracks the input with headroom; without it a model glitch or
    /// an edited prompt could bill for a very long answer.
    /// </summary>
    internal static int CalculateMaxTokens(string text, bool thinkingDisabled)
    {
        // Roughly one token per two characters is pessimistic for Latin text, so this
        // leaves ample room for the answer itself.
        int answerTokens = Math.Clamp(text.Length, 256, 2048);

        // Reasoning tokens are produced before the answer, so thinking mode needs its
        // own budget on top or the answer risks being cut off.
        return thinkingDisabled ? answerTokens : answerTokens + 2048;
    }

    internal static string BuildRequestJson(string model, string prompt, string text, string thinking)
    {
        bool thinkingDisabled = string.Equals(thinking, AppSettings.ThinkingDisabled, StringComparison.Ordinal);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteBoolean("stream", false);
            writer.WriteNumber("max_tokens", CalculateMaxTokens(text, thinkingDisabled));

            // DeepSeek enables thinking at "high" effort unless told otherwise, which
            // costs seconds and tokens that respelling a chat line never needs.
            writer.WriteStartObject("thinking");
            writer.WriteString("type", thinkingDisabled ? "disabled" : "enabled");
            writer.WriteEndObject();

            if (thinkingDisabled)
            {
                // temperature only takes effect outside thinking mode; spelling wants
                // the same answer every time, not a creative one.
                writer.WriteNumber("temperature", 0);
            }
            else
            {
                writer.WriteString("reasoning_effort", thinking);
            }

            writer.WriteStartArray("messages");

            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", prompt);
            writer.WriteEndObject();

            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", text);
            writer.WriteEndObject();

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The answer is only complete when the model stopped on its own. DeepSeek also
    /// reports <c>length</c>, <c>content_filter</c> and <c>insufficient_system_resource</c>,
    /// each of which can carry a half-written sentence, so anything other than
    /// <c>stop</c> is rejected rather than enumerated. A missing value is accepted, since
    /// proxies do not always pass it through.
    /// </summary>
    internal static string? GetIncompleteReason(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            if (!choices[0].TryGetProperty("finish_reason", out JsonElement finishReasonElement))
                return null;

            string? finishReason = finishReasonElement.GetString();
            if (string.IsNullOrWhiteSpace(finishReason) || string.Equals(finishReason, "stop", StringComparison.Ordinal))
                return null;

            return finishReason;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string DescribeIncompleteAnswer(string finishReason) => finishReason switch
    {
        "length" => "DeepSeek's answer was cut short because it got too long, so your text was left alone. Try a shorter text.",
        "content_filter" => "DeepSeek's content filter blocked the answer, so your text was left alone.",
        "insufficient_system_resource" => "DeepSeek ran out of capacity mid-answer, so your text was left alone. Try again shortly.",
        _ => $"DeepSeek stopped before finishing the answer ({finishReason}), so your text was left alone."
    };

    internal static string? ExtractContent(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("choices", out JsonElement choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out JsonElement messageElement)
                || !messageElement.TryGetProperty("content", out JsonElement contentElement))
            {
                return null;
            }

            return contentElement.GetString();
        }
        catch (JsonException ex)
        {
            AppDiagnostics.Warning("Could not parse the DeepSeek response.", ex);
            return null;
        }
    }

    internal static string DescribeFailure(HttpStatusCode statusCode, string responseBody)
    {
        string? apiMessage = TryReadApiErrorMessage(responseBody);

        string summary = statusCode switch
        {
            HttpStatusCode.Unauthorized => "DeepSeek rejected the API key. Check it in Settings.",
            HttpStatusCode.PaymentRequired => "The DeepSeek account is out of credit.",
            HttpStatusCode.TooManyRequests => "DeepSeek is rate limiting the requests. Wait a moment and try again.",
            HttpStatusCode.UnprocessableEntity => "DeepSeek could not process the request. Check the model name in Settings.",
            HttpStatusCode.BadRequest => "DeepSeek rejected the request. Check the model name in Settings.",
            _ when (int)statusCode >= 500 => "DeepSeek is unavailable right now. Try again shortly.",
            _ => $"DeepSeek returned an error ({(int)statusCode})."
        };

        return string.IsNullOrWhiteSpace(apiMessage) ? summary : $"{summary} ({apiMessage})";
    }

    private static string? TryReadApiErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.TryGetProperty("message", out JsonElement message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error bodies are common behind proxies; the status summary is enough.
        }

        return null;
    }

    /// <summary>Removes wrappers a model sometimes adds despite being told not to.</summary>
    internal static string Clean(string content)
    {
        string cleaned = content.Trim();

        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLineBreak = cleaned.IndexOf('\n');
            if (firstLineBreak >= 0)
                cleaned = cleaned[(firstLineBreak + 1)..];

            int closingFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
                cleaned = cleaned[..closingFence];

            cleaned = cleaned.Trim();
        }

        // Only unwrap when the quotes are the outermost pair, so a quoted phrase
        // inside the corrected sentence is never stripped.
        if (cleaned.Length >= 2
            && cleaned[0] == '"'
            && cleaned[^1] == '"'
            && cleaned.IndexOf('"', 1) == cleaned.Length - 1)
        {
            cleaned = cleaned[1..^1];
        }

        return cleaned;
    }

    /// <summary>
    /// Puts back the leading and trailing whitespace of the original text, so a
    /// trailing space the user typed on purpose survives the round trip.
    /// </summary>
    internal static string RestoreOuterWhitespace(string original, string corrected)
    {
        int leading = 0;
        while (leading < original.Length && char.IsWhiteSpace(original[leading]))
            leading++;

        int trailing = 0;
        while (trailing < original.Length - leading && char.IsWhiteSpace(original[^(trailing + 1)]))
            trailing++;

        return string.Concat(
            original.AsSpan(0, leading),
            corrected,
            original.AsSpan(original.Length - trailing));
    }
}
