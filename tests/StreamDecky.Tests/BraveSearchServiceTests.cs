using System.Linq;
using System.Net;
using System.Text;
using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class BraveSearchServiceTests
{
    private const string TestEndpoint = "https://api.search.brave.test/res/v1/web/search";

    [Fact]
    public async Task Search_SendsTheKeyAsASubscriptionTokenAndReturnsTheHits()
    {
        HttpRequestMessage? sent = null;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            sent = request;
            return Json("""
                {
                  "web": {
                    "results": [
                      {
                        "title": "Release notes",
                        "url": "https://example.test/notes",
                        "description": "Version 4 shipped on 12 March.",
                        "age": "3 days ago",
                        "profile": { "name": "Example" }
                      }
                    ]
                  }
                }
                """);
        }));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("version 4 release date", "brave-key");

        Assert.True(result.Success);
        WebSearchHit hit = Assert.Single(result.Hits);
        Assert.Equal("Release notes", hit.Title);
        Assert.Equal("https://example.test/notes", hit.Url);
        Assert.Equal("Version 4 shipped on 12 March.", hit.Description);
        Assert.Equal("Example", hit.SiteName);
        Assert.Equal("3 days ago", hit.Age);

        Assert.Equal("brave-key", Assert.Single(sent!.Headers.GetValues("X-Subscription-Token")));
        Assert.Contains("q=version%204%20release%20date", sent.RequestUri!.OriginalString);
    }

    [Fact]
    public async Task Search_AsksBraveToLeaveOutTheHighlightMarkup()
    {
        HttpRequestMessage? sent = null;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            sent = request;
            return Json("""{"web":{"results":[{"title":"T","url":"https://a.test/","description":"D"}]}}""");
        }));

        await new BraveSearchService(client, TestEndpoint).SearchAsync("anything", "brave-key");

        Assert.Contains("text_decorations=0", sent!.RequestUri!.Query);
    }

    /// <summary>
    /// A stray tag in a description would travel straight into the prompt and onto a
    /// source chip, so it is stripped even though the request asks Brave not to send it.
    /// </summary>
    [Fact]
    public async Task Search_StripsMarkupAndDecodesEntitiesInTheText()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json("""
            {"web":{"results":[{
              "title":"Ben &amp; Co",
              "url":"https://a.test/",
              "description":"The <strong>fastest</strong> lap was 1:32."
            }]}}
            """)));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("fastest lap", "brave-key");

        WebSearchHit hit = Assert.Single(result.Hits);
        Assert.Equal("Ben & Co", hit.Title);
        Assert.Equal("The fastest lap was 1:32.", hit.Description);
    }

    /// <summary>
    /// News results carry a date and are what a question about something current should
    /// lean on, so they are offered to the model first.
    /// </summary>
    [Fact]
    public async Task Search_PutsNewsResultsAheadOfWebResults()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json("""
            {
              "web": {"results":[{"title":"Wiki page","url":"https://wiki.test/","description":"Background."}]},
              "news": {"results":[{"title":"Result last night","url":"https://news.test/","description":"They won 3-1."}]}
            }
            """)));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("who won", "brave-key");

        Assert.Equal(2, result.Hits.Count);
        Assert.Equal("https://news.test/", result.Hits[0].Url);
        Assert.Equal("https://wiki.test/", result.Hits[1].Url);
    }

    [Fact]
    public async Task Search_DropsDuplicateAndUnlinkableResults()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json("""
            {
              "web": {"results":[
                {"title":"Same","url":"https://a.test/","description":"One."},
                {"title":"Same again","url":"https://a.test/","description":"Two."},
                {"title":"No link","description":"Three."},
                {"url":"https://b.test/","description":"No title."}
              ]}
            }
            """)));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("anything", "brave-key");

        WebSearchHit hit = Assert.Single(result.Hits);
        Assert.Equal("https://a.test/", hit.Url);
    }

    [Fact]
    public async Task Search_FallsBackToTheHostWhenBraveNamesNoSite()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json(
            """{"web":{"results":[{"title":"T","url":"https://docs.example.test/page","description":"D"}]}}""")));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("anything", "brave-key");

        Assert.Equal("docs.example.test", Assert.Single(result.Hits).SiteName);
    }

    [Fact]
    public async Task Search_RefusesWithoutAKeyAndNeverCallsBrave()
    {
        bool called = false;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            called = true;
            return Json("{}");
        }));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("anything", "   ");

        Assert.False(result.Success);
        Assert.Contains("Brave Search API key", result.ErrorMessage);
        Assert.False(called);
    }

    [Fact]
    public async Task Search_ReportsAnEmptyResultSetRatherThanSucceedingWithNothing()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Json("""{"web":{"results":[]}}""")));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("anything", "brave-key");

        Assert.False(result.Success);
        Assert.Contains("found nothing", result.ErrorMessage);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "rejected the API key")]
    [InlineData(HttpStatusCode.Forbidden, "rejected the API key")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limiting")]
    [InlineData(HttpStatusCode.PaymentRequired, "plan")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unavailable")]
    public void DescribeFailure_ExplainsWhatTheUserCanDoAboutIt(HttpStatusCode statusCode, string expected)
    {
        Assert.Contains(expected, BraveSearchService.DescribeFailure(statusCode, "{}"));
    }

    [Fact]
    public void DescribeFailure_AppendsBravesOwnMessageWhenThereIsOne()
    {
        string message = BraveSearchService.DescribeFailure(
            HttpStatusCode.Unauthorized,
            """{"error":{"detail":"Subscription token invalid"}}""");

        Assert.Contains("rejected the API key", message);
        Assert.Contains("Subscription token invalid", message);
    }

    [Fact]
    public void ParseHits_SurvivesAResponseThatIsNotTheShapeItExpects()
    {
        Assert.Empty(BraveSearchService.ParseHits("not json at all"));
        Assert.Empty(BraveSearchService.ParseHits("[]"));
        Assert.Empty(BraveSearchService.ParseHits("""{"web":"unexpected"}"""));
        Assert.Empty(BraveSearchService.ParseHits("""{"web":{"results":"unexpected"}}"""));
    }


    /// <summary>
    /// A key belonging to another Brave product does not fail: it answers 200 with an
    /// envelope carrying only <c>type</c> and <c>query</c>. Reporting that as "found
    /// nothing" sends the user off rewording a question that was never the problem.
    /// <para>
    /// The headers here are the ones a real key for the wrong product returns.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Search_PointsAtTheWrongKindOfKeyRatherThanBlamingTheQuestion()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            HttpResponseMessage response = Json("""{"type":"search","query":{"original":"streamdecky"}}""");
            response.Headers.Add("x-ratelimit-limit", "2, 0");
            response.Headers.Add("x-ratelimit-remaining", "1, 0");
            return response;
        }));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("streamdecky", "brave-key");

        Assert.False(result.Success);
        Assert.Contains("not a Web Search key", result.ErrorMessage);
        Assert.Contains("separate key per product", result.ErrorMessage);
        Assert.DoesNotContain("rewording", result.ErrorMessage);
    }

    /// <summary>
    /// An empty <c>results</c> array is a genuinely empty result set, and still deserves
    /// the "try rewording" advice the wrong-key case must not get.
    /// </summary>
    [Fact]
    public async Task Search_StillReportsAGenuinelyEmptyResultSetAsNothingFound()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            HttpResponseMessage response = Json("""{"type":"search","web":{"results":[]}}""");
            response.Headers.Add("x-ratelimit-limit", "50, 0");
            response.Headers.Add("x-ratelimit-remaining", "49, 0");
            return response;
        }));

        WebSearchResult result = await new BraveSearchService(client, TestEndpoint)
            .SearchAsync("asdfqwerzxcv", "brave-key");

        Assert.False(result.Success);
        Assert.Contains("found nothing", result.ErrorMessage);
    }

    [Fact]
    public void HasResultSections_TellsAnEmptyEnvelopeFromAnEmptyResultSet()
    {
        Assert.False(BraveSearchService.HasResultSections("""{"type":"search","query":{"original":"x"}}"""));
        Assert.False(BraveSearchService.HasResultSections("not json"));

        Assert.True(BraveSearchService.HasResultSections("""{"web":{"results":[]}}"""));
        Assert.True(BraveSearchService.HasResultSections("""{"news":{"results":[]}}"""));
    }

    /// <summary>
    /// A working free-tier key reports a monthly window of zero, so a zero limit means
    /// "not metered on this window" rather than "no quota". Reading it the other way
    /// would blame the subscription for a key that is merely for the wrong product.
    /// </summary>
    [Theory]
    [InlineData("2, 0", "1, 0")]     // real headers from a key made for another product
    [InlineData("50, 0", "49, 0")]   // real headers from a working Web Search key
    [InlineData("0", "0")]
    [InlineData(null, null)]
    [InlineData("nonsense", "nonsense")]
    public void DescribeEmptyResponse_BlamesTheKeyWhenNoWindowIsActuallySpent(
        string? limitHeader,
        string? remainingHeader)
    {
        Assert.Contains(
            "not a Web Search key",
            BraveSearchService.DescribeEmptyResponse(limitHeader, remainingHeader));
    }

    [Theory]
    [InlineData("50, 2000", "0, 1997")]   // the per-second window is spent
    [InlineData("50, 2000", "49, 0")]     // the monthly window is spent
    public void DescribeEmptyResponse_ReportsAMeteredWindowThatIsActuallySpent(
        string limitHeader,
        string remainingHeader)
    {
        Assert.Contains(
            "quota is used up",
            BraveSearchService.DescribeEmptyResponse(limitHeader, remainingHeader));
    }

    /// <summary>
    /// Only the "always search" path reaches this: it searches the question verbatim, and
    /// a question may be half as long again as Brave accepts. Cutting mid-word would hand
    /// Brave a fragment.
    /// </summary>
    [Fact]
    public void Shorten_CutsOnAWordBoundaryWhenTheQueryIsTooLong()
    {
        string query = string.Join(' ', Enumerable.Repeat("alpha", 200));

        string shortened = BraveSearchService.Shorten(query);

        Assert.True(shortened.Length <= BraveSearchService.MaxQueryLength);
        Assert.EndsWith("alpha", shortened, StringComparison.Ordinal);
        Assert.Equal(shortened.TrimEnd(), shortened);
    }

    [Fact]
    public void Shorten_LeavesAQueryInsideTheLimitExactlyAsItIs()
    {
        Assert.Equal("who won last night", BraveSearchService.Shorten("who won last night"));
    }

    /// <summary>A single unbroken token has no boundary to cut on, so it is cut hard.</summary>
    [Fact]
    public void Shorten_FallsBackToAHardCutWhenThereIsNoWordBoundary()
    {
        string shortened = BraveSearchService.Shorten(new string('a', BraveSearchService.MaxQueryLength + 50));

        Assert.Equal(BraveSearchService.MaxQueryLength, shortened.Length);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
