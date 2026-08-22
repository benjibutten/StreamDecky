using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StreamDecky.Helpers;

namespace StreamDecky.Services;

/// <summary>One web result, trimmed down to what a short answer and its source chip need.</summary>
public sealed record WebSearchHit(string Title, string Url, string Description, string? SiteName, string? Age);

/// <summary>Outcome of one Brave query. <see cref="Hits"/> is only meaningful when <see cref="Success"/>.</summary>
public sealed record WebSearchResult(bool Success, IReadOnlyList<WebSearchHit> Hits, string? ErrorMessage)
{
    public static WebSearchResult Ok(IReadOnlyList<WebSearchHit> hits) => new(true, hits, null);

    public static WebSearchResult Fail(string message) => new(false, Array.Empty<WebSearchHit>(), message);
}

/// <summary>
/// Queries the Brave Search API for the overlay's quick answers. Only the plain web
/// search endpoint is used, not Brave's own summarizer: DeepSeek writes the answer, so
/// what this needs back is snippets and links, and the plain endpoint is on every plan.
/// </summary>
public class BraveSearchService
{
    public const string DefaultEndpoint = "https://api.search.brave.com/res/v1/web/search";

    /// <summary>
    /// Enough context for a two-sentence answer without burying the model, and short
    /// enough that the second DeepSeek call stays cheap.
    /// </summary>
    public const int MaxHits = 6;

    /// <summary>Brave rejects anything longer, and a question typed on stream is never near it.</summary>
    public const int MaxQueryLength = 400;

    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    public BraveSearchService(HttpClient? httpClient = null, string? endpoint = null)
    {
        // Only the client we own gets its timeout adjusted; HttpClient forbids changing
        // that once an injected instance has sent a request.
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint;
    }

    public async Task<WebSearchResult> SearchAsync(
        string query,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return WebSearchResult.Fail("There is nothing to search for.");

        if (string.IsNullOrWhiteSpace(apiKey))
            return WebSearchResult.Fail("Add your Brave Search API key in Settings first.");

        string trimmedQuery = Shorten(query.Trim());

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(_endpoint, trimmedQuery));
            request.Headers.Add("X-Subscription-Token", apiKey.Trim());
            request.Headers.Add("Accept", "application/json");

            using HttpResponseMessage response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AppDiagnostics.Warning(
                    $"Brave search returned {(int)response.StatusCode}. Body: {Excerpt(responseBody)}");
                return WebSearchResult.Fail(DescribeFailure(response.StatusCode, responseBody));
            }

            // A 200 carrying no result sections at all is not an empty result set. Brave
            // answers that way when the subscription grants no quota, so reporting it as
            // "nothing found" would send the user off rewording a question that was fine.
            if (!HasResultSections(responseBody))
            {
                string message = DescribeEmptyResponse(
                    FirstHeaderValue(response, "x-ratelimit-limit"),
                    FirstHeaderValue(response, "x-ratelimit-remaining"));

                AppDiagnostics.Warning(
                    $"Brave search returned 200 with no result sections. "
                    + $"limit='{FirstHeaderValue(response, "x-ratelimit-limit")}' "
                    + $"remaining='{FirstHeaderValue(response, "x-ratelimit-remaining")}' "
                    + $"policy='{FirstHeaderValue(response, "x-ratelimit-policy")}' "
                    + $"Body: {Excerpt(responseBody)}");

                return WebSearchResult.Fail(message);
            }

            IReadOnlyList<WebSearchHit> hits = ParseHits(responseBody);
            if (hits.Count == 0)
                return WebSearchResult.Fail("Brave found nothing for that. Try rewording the question.");

            return WebSearchResult.Ok(hits);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WebSearchResult.Fail("The search was cancelled.");
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces its own timeout as a cancellation with no token set.
            return WebSearchResult.Fail("Brave did not answer in time. Try again.");
        }
        catch (HttpRequestException ex)
        {
            AppDiagnostics.Warning("Brave search request failed.", ex);
            return WebSearchResult.Fail("Could not reach Brave Search. Check your internet connection.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning("Brave search failed unexpectedly.", ex);
            return WebSearchResult.Fail("The search failed. See the log for details.");
        }
    }

    /// <summary>
    /// Brings a query inside Brave's length limit, cutting at a word boundary so the tail
    /// end is a whole word rather than half of one.
    /// <para>
    /// Only the <see cref="Models.AppSettings.SearchModeAlways"/> path can reach this: it
    /// searches the question verbatim, and a question may be half as long again as Brave
    /// accepts. When the model writes the query it produces keywords nowhere near the
    /// limit. Refusing the question outright would be worse — the answer call can still
    /// use the whole question, and the shortened query is only what finds the sources —
    /// but it is logged, because a silently reworded search is a confusing thing to debug.
    /// </para>
    /// </summary>
    internal static string Shorten(string query)
    {
        if (query.Length <= MaxQueryLength)
            return query;

        int lastSpace = query.LastIndexOf(' ', MaxQueryLength - 1);
        string shortened = (lastSpace > MaxQueryLength / 2 ? query[..lastSpace] : query[..MaxQueryLength]).TrimEnd();

        AppDiagnostics.Warning(
            $"A search query of {query.Length} characters was shortened to {shortened.Length} to fit Brave's limit of {MaxQueryLength}.");

        return shortened;
    }

    internal static string BuildRequestUri(string endpoint, string query)
    {
        var parameters = new StringBuilder();
        parameters.Append("q=").Append(Uri.EscapeDataString(query));
        parameters.Append("&count=").Append(MaxHits);

        // Brave wraps matched terms in <strong> unless told not to, and that markup would
        // travel straight into the prompt and the source chips.
        parameters.Append("&text_decorations=0");
        parameters.Append("&safesearch=moderate");

        // No result_filter on purpose. Narrowing to web+news would trim the response, but
        // which sections a plan may ask for varies by tier, and a filter the plan does not
        // cover is rejected outright. Reading whatever sections come back costs a little
        // bandwidth and cannot fail.

        char separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{endpoint}{separator}{parameters}";
    }

    /// <summary>
    /// Reads web and news results into one list. Every field is optional on purpose:
    /// Brave omits plenty of them per result, and a missing description costs far less
    /// than dropping the result entirely.
    /// </summary>
    internal static IReadOnlyList<WebSearchHit> ParseHits(string responseBody)
    {
        var hits = new List<WebSearchHit>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return hits;

            // News first: when a question is about something current, the dated result is
            // the one the answer should lean on.
            AppendResults(root, "news", hits, seenUrls);
            AppendResults(root, "web", hits, seenUrls);
        }
        catch (JsonException ex)
        {
            AppDiagnostics.Warning("Could not parse the Brave search response.", ex);
        }

        return hits;
    }

    private static void AppendResults(
        JsonElement root,
        string sectionName,
        List<WebSearchHit> hits,
        HashSet<string> seenUrls)
    {
        if (!root.TryGetProperty(sectionName, out JsonElement section)
            || section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty("results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement result in results.EnumerateArray())
        {
            if (hits.Count >= MaxHits)
                return;

            if (result.ValueKind != JsonValueKind.Object)
                continue;

            string? url = ReadString(result, "url");
            string? title = ReadString(result, "title");

            // A result with no link cannot be shown as a source, so it must not be allowed
            // to back an answer either.
            if (url == null || title == null)
                continue;

            if (!seenUrls.Add(url))
                continue;

            hits.Add(new WebSearchHit(
                title,
                url,
                ReadString(result, "description") ?? string.Empty,
                ReadSiteName(result, url),
                ReadString(result, "age")));
        }
    }

    private static string? ReadSiteName(JsonElement result, string url)
    {
        if (result.TryGetProperty("profile", out JsonElement profile)
            && profile.ValueKind == JsonValueKind.Object)
        {
            string? name = ReadString(profile, "name") ?? ReadString(profile, "long_name");
            if (name != null)
                return name;
        }

        if (result.TryGetProperty("meta_url", out JsonElement metaUrl)
            && metaUrl.ValueKind == JsonValueKind.Object)
        {
            string? hostname = ReadString(metaUrl, "hostname");
            if (hostname != null)
                return hostname;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.Host : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? raw = value.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // text_decorations=0 asks Brave to leave the markup out, but stripping it here too
        // means a stray tag can never reach the prompt or a source chip.
        string cleaned = WebUtility.HtmlDecode(HtmlTag.Replace(raw, string.Empty)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    /// <summary>
    /// True when the response carries a results section at all. Brave includes
    /// <c>web</c> (with a possibly empty <c>results</c> array) for any query it actually
    /// ran; a body with neither <c>web</c> nor <c>news</c> means it never ran one.
    /// </summary>
    internal static bool HasResultSections(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && ((root.TryGetProperty("web", out JsonElement web) && web.ValueKind == JsonValueKind.Object)
                    || (root.TryGetProperty("news", out JsonElement news) && news.ValueKind == JsonValueKind.Object));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Explains a 200 that carried no results at all.
    /// <para>
    /// The rate limit headers list one value per policy window, comma separated. They are
    /// a poor entitlement signal on their own: a working free-tier key reports
    /// <c>limit: 50, 0</c>, so a zero window means "not metered here", not "no quota". Only
    /// a window with a real limit and nothing left says anything definite.
    /// </para>
    /// <para>
    /// Everything else lands on the cause that actually produces this response: Brave
    /// issues a separate key per product, and a key for another product authenticates
    /// fine but returns an empty envelope.
    /// </para>
    /// </summary>
    internal static string DescribeEmptyResponse(string? limitHeader, string? remainingHeader)
    {
        if (IsQuotaExhausted(limitHeader, remainingHeader))
            return "The Brave Search quota is used up for now. It resets with your plan's period.";

        return "Brave answered without any results, which almost always means this key is not a Web Search key. "
            + "Brave issues a separate key per product, so a key made for Answers will not work here — "
            + "create one under Web Search at api-dashboard.search.brave.com.";
    }

    /// <summary>
    /// True when a policy window that actually meters requests has none left. Windows are
    /// paired positionally across the two headers.
    /// </summary>
    private static bool IsQuotaExhausted(string? limitHeader, string? remainingHeader)
    {
        int[] limits = ParseHeaderNumbers(limitHeader);
        int[] remaining = ParseHeaderNumbers(remainingHeader);

        for (int window = 0; window < Math.Min(limits.Length, remaining.Length); window++)
        {
            if (limits[window] > 0 && remaining[window] == 0)
                return true;
        }

        return false;
    }

    private static int[] ParseHeaderNumbers(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return Array.Empty<int>();

        return headerValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out int value) ? value : -1)
            .Where(value => value >= 0)
            .ToArray();
    }

    private static string? FirstHeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;

    /// <summary>Keeps a logged body short enough to be useful without filling the log.</summary>
    private static string Excerpt(string body) =>
        string.IsNullOrEmpty(body) ? "(empty)"
        : body.Length <= 400 ? body
        : body[..400] + "…";

    internal static string DescribeFailure(HttpStatusCode statusCode, string responseBody)
    {
        string? apiMessage = TryReadApiErrorMessage(responseBody);

        string summary = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "Brave rejected the API key. Check it in Settings.",
            HttpStatusCode.TooManyRequests =>
                "Brave is rate limiting the searches. Wait a second and try again.",
            HttpStatusCode.UnprocessableEntity =>
                "Brave could not process that search. Try rewording the question.",
            HttpStatusCode.PaymentRequired =>
                "The Brave Search plan does not cover this request. Check your plan in the Brave dashboard.",
            _ when (int)statusCode >= 500 =>
                "Brave Search is unavailable right now. Try again shortly.",
            _ => $"Brave Search returned an error ({(int)statusCode})."
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
            if (!document.RootElement.TryGetProperty("error", out JsonElement error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (error.TryGetProperty("detail", out JsonElement detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString();

            if (error.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String)
                return message.GetString();
        }
        catch (JsonException)
        {
            // Non-JSON error bodies are common behind proxies; the status summary is enough.
        }

        return null;
    }
}
