using System.Net;
using System.Text.Json;
using StreamDecky.Helpers;

namespace StreamDecky.Services;

/// <summary>
/// The parts of DeepSeek's OpenAI-compatible chat completions protocol that both the
/// spell checker and the quick answer service need: reading a choice out of a response
/// and turning a failure into something worth showing a user.
/// </summary>
internal static class DeepSeekProtocol
{
    public const string ChatCompletionsEndpoint = "https://api.deepseek.com/chat/completions";

    /// <summary>
    /// The model's own reason for stopping, or null when the response does not carry one.
    /// Proxies do not always pass it through, so a missing value is not an error.
    /// </summary>
    public static string? GetFinishReason(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (!TryGetFirstChoice(document.RootElement, out JsonElement choice))
                return null;

            if (!choice.TryGetProperty("finish_reason", out JsonElement finishReason))
                return null;

            string? value = finishReason.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ExtractContent(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (!TryGetFirstChoice(document.RootElement, out JsonElement choice))
                return null;

            if (!choice.TryGetProperty("message", out JsonElement message)
                || !message.TryGetProperty("content", out JsonElement content))
            {
                return null;
            }

            return content.GetString();
        }
        catch (JsonException ex)
        {
            AppDiagnostics.Warning("Could not parse the DeepSeek response.", ex);
            return null;
        }
    }

    public static string DescribeFailure(HttpStatusCode statusCode, string responseBody)
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

    public static string? TryReadApiErrorMessage(string responseBody)
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
    public static string Clean(string content)
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

        // Only unwrap when the quotes are the outermost pair, so a quoted phrase inside
        // the text is never stripped.
        if (cleaned.Length >= 2
            && cleaned[0] == '"'
            && cleaned[^1] == '"'
            && cleaned.IndexOf('"', 1) == cleaned.Length - 1)
        {
            cleaned = cleaned[1..^1];
        }

        return cleaned;
    }

    private static bool TryGetFirstChoice(JsonElement root, out JsonElement choice)
    {
        choice = default;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("choices", out JsonElement choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return false;
        }

        choice = choices[0];
        return true;
    }
}
