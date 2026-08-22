using System.Net;
using System.Text;
using System.Text.Json;
using StreamDecky.Models;
using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class DeepSeekSpellCheckServiceTests
{
    private const string Endpoint = "https://api.deepseek.test/chat/completions";

    [Fact]
    public async Task CorrectAsync_SendsPromptAndTextAsSeparateMessages()
    {
        string? requestBody = null;
        string? authorization = null;

        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            authorization = request.Headers.Authorization?.ToString();
            return Completion("Hello there");
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync("helo ther", "sk-test-key", "deepseek-v4-flash", "Fix the spelling.");

        Assert.True(result.Success);
        Assert.Equal("Hello there", result.Text);
        Assert.Equal("Bearer sk-test-key", authorization);

        using JsonDocument document = JsonDocument.Parse(requestBody!);
        JsonElement root = document.RootElement;
        Assert.Equal("deepseek-v4-flash", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());

        JsonElement messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Fix the spelling.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("helo ther", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CorrectAsync_DisablesThinkingByDefaultAndPinsTemperature()
    {
        string? requestBody = null;

        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Completion("ok");
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        await service.CorrectAsync("text", "sk-test-key");

        using JsonDocument document = JsonDocument.Parse(requestBody!);
        JsonElement root = document.RootElement;

        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(0, root.GetProperty("temperature").GetDouble());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
    }

    [Theory]
    [InlineData("low")]
    [InlineData("high")]
    [InlineData("max")]
    public async Task CorrectAsync_EnablesThinkingAtTheRequestedEffort(string level)
    {
        string? requestBody = null;

        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Completion("ok");
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        await service.CorrectAsync("text", "sk-test-key", thinking: level);

        using JsonDocument document = JsonDocument.Parse(requestBody!);
        JsonElement root = document.RootElement;

        Assert.Equal("enabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(level, root.GetProperty("reasoning_effort").GetString());

        // temperature is inert in thinking mode, so it is left off the request entirely.
        Assert.False(root.TryGetProperty("temperature", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public async Task CorrectAsync_FallsBackToNoThinkingForUnknownLevels(string? level)
    {
        string? requestBody = null;

        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Completion("ok");
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        await service.CorrectAsync("text", "sk-test-key", thinking: level);

        using JsonDocument document = JsonDocument.Parse(requestBody!);
        Assert.Equal("disabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task CorrectAsync_CapsTheAnswerSizeRelativeToTheInput()
    {
        string? requestBody = null;

        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Completion("ok");
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        await service.CorrectAsync(new string('a', 900), "sk-test-key");

        using JsonDocument document = JsonDocument.Parse(requestBody!);
        Assert.Equal(900, document.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Theory]
    [InlineData(10, true, 256)]      // floor for short lines
    [InlineData(900, true, 900)]     // tracks the input
    [InlineData(4000, true, 2048)]   // ceiling
    [InlineData(900, false, 2948)]   // thinking gets its own budget on top
    public void CalculateMaxTokens_StaysWithinTheFloorAndCeiling(int length, bool thinkingDisabled, int expected)
    {
        Assert.Equal(expected, DeepSeekSpellCheckService.CalculateMaxTokens(new string('a', length), thinkingDisabled));
    }

    [Theory]
    [InlineData("length", "cut short")]
    [InlineData("content_filter", "content filter")]
    [InlineData("insufficient_system_resource", "ran out of capacity")]
    [InlineData("something_new", "stopped before finishing")]
    public async Task CorrectAsync_DiscardsAnyAnswerTheModelDidNotFinish(string finishReason, string expectedMessage)
    {
        string json = ChoiceJson("\"" + finishReason + "\"", "Half a senten");

        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync("half a senten", "sk-test-key");

        Assert.False(result.Success);
        Assert.Contains(expectedMessage, result.ErrorMessage);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData(null)]
    public async Task CorrectAsync_AcceptsAFinishedAnswer(string? finishReason)
    {
        string json = ChoiceJson(
            finishReason == null ? "null" : "\"" + finishReason + "\"",
            "A whole sentence");

        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync("a whol sentence", "sk-test-key");

        Assert.True(result.Success);
        Assert.Equal("A whole sentence", result.Text);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("```\n\n```")]
    [InlineData("```text\n   \n```")]
    public async Task CorrectAsync_FailsWhenCleaningLeavesNothing(string content)
    {
        // These pass the raw emptiness check but clean away to nothing, and must never
        // be written back over the user's text.
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Completion(content)));
        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync("helo", "sk-test-key");

        Assert.False(result.Success);
        Assert.Contains("empty answer", result.ErrorMessage);
    }

    [Fact]
    public async Task CorrectAsync_FallsBackToDefaultModelAndPrompt()
    {
        string? requestBody = null;

        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Completion("ok");
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        await service.CorrectAsync("text", "sk-test-key", model: "   ", prompt: "  ");

        using JsonDocument document = JsonDocument.Parse(requestBody!);
        Assert.Equal(AppSettings.DefaultDeepSeekModel, document.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            AppSettings.DefaultSpellCheckPrompt,
            document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CorrectAsync_KeepsLeadingAndTrailingWhitespaceFromTheOriginal()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Completion("hello")));
        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync("  helo ", "sk-test-key");

        Assert.Equal("  hello ", result.Text);
    }

    [Fact]
    public async Task CorrectAsync_RejectsMissingApiKeyWithoutCallingTheApi()
    {
        bool called = false;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            called = true;
            return Completion("never");
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync("some text", "   ");

        Assert.False(called);
        Assert.False(result.Success);
        Assert.Contains("API key", result.ErrorMessage);
    }

    [Fact]
    public async Task CorrectAsync_RejectsTextLongerThanTheLimit()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Completion("never")));
        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync(
            new string('a', DeepSeekSpellCheckService.MaxInputLength + 1),
            "sk-test-key");

        Assert.False(result.Success);
        Assert.Contains("too long", result.ErrorMessage);
    }

    [Fact]
    public async Task CorrectAsync_ReportsTheApiErrorMessageOnUnauthorized()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"Authentication Fails"}}""", Encoding.UTF8, "application/json")
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync("text", "sk-bad-key");

        Assert.False(result.Success);
        Assert.Contains("rejected the API key", result.ErrorMessage);
        Assert.Contains("Authentication Fails", result.ErrorMessage);
    }

    [Fact]
    public async Task CorrectAsync_FailsWhenTheAnswerHasNoChoices()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[]}""", Encoding.UTF8, "application/json")
        }));

        var service = new DeepSeekSpellCheckService(client, Endpoint);

        SpellCheckResult result = await service.CorrectAsync("text", "sk-test-key");

        Assert.False(result.Success);
        Assert.Contains("empty answer", result.ErrorMessage);
    }

    [Theory]
    [InlineData("Hello there", "Hello there")]
    [InlineData("\"Hello there\"", "Hello there")]
    [InlineData("```\nHello there\n```", "Hello there")]
    [InlineData("```text\nHello there\n```", "Hello there")]
    [InlineData("He said \"hi\" to me", "He said \"hi\" to me")]
    public void Clean_StripsOnlyWrappersTheModelAdded(string content, string expected)
    {
        Assert.Equal(expected, DeepSeekSpellCheckService.Clean(content));
    }

    [Theory]
    [InlineData("\n  text \n", "fixed", "\n  fixed \n")]
    [InlineData("text", "fixed", "fixed")]
    [InlineData("   ", "fixed", "   fixed")]
    public void RestoreOuterWhitespace_DoesNotDuplicateWhitespace(string original, string corrected, string expected)
    {
        Assert.Equal(expected, DeepSeekSpellCheckService.RestoreOuterWhitespace(original, corrected));
    }

    /// <summary>One choice with an explicit finish_reason, which <see cref="Completion"/> omits.</summary>
    private static string ChoiceJson(string finishReasonJson, string content)
    {
        return "{\"choices\":[{\"finish_reason\":" + finishReasonJson
            + ",\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(content) + "}}]}";
    }

    private static HttpResponseMessage Completion(string content)
    {
        string json = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content } }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
