using System.Net;
using System.Text;
using System.Text.Json;
using StreamDecky.Models;
using StreamDecky.Services;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class TextHelperWidgetViewModelTests
{
    [Fact]
    public async Task SpellCheck_ReplacesTheTextAndOffersAnUndo()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Completion("I walked home")));
        using var widget = new TextHelperWidgetViewModel(settings, new DeepSeekSpellCheckService(client, TestEndpoint))
        {
            Text = "i walkd hom"
        };

        await widget.SpellCheckCommand.ExecuteAsync(null);

        Assert.Equal("I walked home", widget.Text);
        Assert.True(widget.CanUndoSpellCheck);
        Assert.False(widget.IsStatusError);
        Assert.False(widget.IsBusy);

        widget.UndoSpellCheckCommand.Execute(null);

        Assert.Equal("i walkd hom", widget.Text);
        Assert.False(widget.CanUndoSpellCheck);
    }

    [Fact]
    public async Task SpellCheck_SendsTheThinkingLevelFromSettings()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        settings.SpellCheckThinking = "low";

        string? requestBody = null;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Completion("fixed");
        }));
        using var widget = new TextHelperWidgetViewModel(settings, new DeepSeekSpellCheckService(client, TestEndpoint))
        {
            Text = "fixt"
        };

        await widget.SpellCheckCommand.ExecuteAsync(null);

        using JsonDocument document = JsonDocument.Parse(requestBody!);
        Assert.Equal("enabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("low", document.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task SpellCheck_AsksForNoThinkingOutOfTheBox()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");

        string? requestBody = null;
        using var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Completion("fixed");
        }));
        using var widget = new TextHelperWidgetViewModel(settings, new DeepSeekSpellCheckService(client, TestEndpoint))
        {
            Text = "fixt"
        };

        await widget.SpellCheckCommand.ExecuteAsync(null);

        using JsonDocument document = JsonDocument.Parse(requestBody!);
        Assert.Equal("disabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SpellCheck_DropsTheCorrectionWhenTheUserKeptTyping()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");

        TextHelperWidgetViewModel? widget = null;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            // Stand in for the user typing on while the request is in flight.
            widget!.Text = "i walkd hom yesterday";
            return Completion("I walked home");
        }));

        widget = new TextHelperWidgetViewModel(settings, new DeepSeekSpellCheckService(client, TestEndpoint))
        {
            Text = "i walkd hom"
        };

        using (widget)
        {
            await widget.SpellCheckCommand.ExecuteAsync(null);

            Assert.Equal("i walkd hom yesterday", widget.Text);
            Assert.False(widget.CanUndoSpellCheck);
            Assert.Contains("changed the text", widget.StatusText);
        }
    }

    [Fact]
    public async Task SpellCheck_DoesNotRefillABoxTheUserCleared()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");

        TextHelperWidgetViewModel? widget = null;
        using var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            widget!.ClearCommand.Execute(null);
            return Completion("I walked home");
        }));

        widget = new TextHelperWidgetViewModel(settings, new DeepSeekSpellCheckService(client, TestEndpoint))
        {
            Text = "i walkd hom"
        };

        using (widget)
        {
            await widget.SpellCheckCommand.ExecuteAsync(null);

            Assert.Equal(string.Empty, widget.Text);
            Assert.False(widget.CanUndoSpellCheck);
        }
    }

    [Fact]
    public async Task SpellCheck_ReportsWhenNothingNeededChanging()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Completion("Already fine")));
        using var widget = new TextHelperWidgetViewModel(settings, new DeepSeekSpellCheckService(client, TestEndpoint))
        {
            Text = "Already fine"
        };

        await widget.SpellCheckCommand.ExecuteAsync(null);

        Assert.Equal("Already fine", widget.Text);
        Assert.False(widget.CanUndoSpellCheck);
        Assert.Contains("Already spelled correctly", widget.StatusText);
    }

    [Fact]
    public async Task SpellCheck_SurfacesTheApiErrorAndLeavesTheTextAlone()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-bad-key");
        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        }));
        using var widget = new TextHelperWidgetViewModel(settings, new DeepSeekSpellCheckService(client, TestEndpoint))
        {
            Text = "helo"
        };

        await widget.SpellCheckCommand.ExecuteAsync(null);

        Assert.Equal("helo", widget.Text);
        Assert.True(widget.IsStatusError);
        Assert.Contains("rejected the API key", widget.StatusText);
        Assert.False(widget.CanUndoSpellCheck);
    }

    [Fact]
    public async Task EditingAfterASpellCheck_DropsTheUndoOffer()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var client = new HttpClient(new StubHttpMessageHandler(_ => Completion("I walked home")));
        using var widget = new TextHelperWidgetViewModel(settings, new DeepSeekSpellCheckService(client, TestEndpoint))
        {
            Text = "i walkd hom"
        };

        await widget.SpellCheckCommand.ExecuteAsync(null);
        Assert.True(widget.CanUndoSpellCheck);

        widget.Text = "I walked home today";

        Assert.False(widget.CanUndoSpellCheck);
        Assert.False(widget.UndoSpellCheckCommand.CanExecute(null));
        Assert.Equal(string.Empty, widget.StatusText);
    }

    [Fact]
    public void SpellCheckAndCopy_AreDisabledWhileTheBoxIsEmpty()
    {
        using var directory = new TemporaryDirectory();
        var settings = new AppSettingsService(directory.Path);
        using var widget = new TextHelperWidgetViewModel(settings);

        Assert.False(widget.SpellCheckCommand.CanExecute(null));
        Assert.False(widget.CopyCommand.CanExecute(null));

        widget.Text = "something";

        Assert.True(widget.SpellCheckCommand.CanExecute(null));
        Assert.True(widget.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void Copy_WorksWithoutRunningTheSpellCheck()
    {
        RunOnStaThread(() =>
        {
            using var directory = new TemporaryDirectory();
            var settings = new AppSettingsService(directory.Path);
            using var widget = new TextHelperWidgetViewModel(settings)
            {
                Text = "raw unchecked text"
            };

            widget.CopyCommand.Execute(null);

            Assert.False(widget.IsStatusError);
            Assert.Contains("Copied", widget.StatusText);
            Assert.Equal("raw unchecked text", System.Windows.Clipboard.GetText());
        });
    }

    [Fact]
    public void Clear_EmptiesTheBoxAndForgetsTheUndo()
    {
        using var directory = new TemporaryDirectory();
        var settings = new AppSettingsService(directory.Path);
        using var widget = new TextHelperWidgetViewModel(settings) { Text = "something" };

        widget.ClearCommand.Execute(null);

        Assert.Equal(string.Empty, widget.Text);
        Assert.False(widget.CanUndoSpellCheck);
        Assert.Equal(string.Empty, widget.StatusText);
    }

    [Fact]
    public void IsApiKeyMissing_TracksTheStoredKey()
    {
        using var directory = new TemporaryDirectory();
        var settings = new AppSettingsService(directory.Path);
        using var widget = new TextHelperWidgetViewModel(settings);

        Assert.True(widget.IsApiKeyMissing);

        Assert.True(settings.TrySetDeepSeekApiKey("sk-test-key", out _));
        widget.Refresh();

        Assert.False(widget.IsApiKeyMissing);
    }

    /// <summary>
    /// The rule the whole two-button design rests on: asking produces something new
    /// beside the text, it never rewrites the text the way spell checking does.
    /// </summary>
    [Fact]
    public async Task Ask_LeavesTheWrittenTextCompletelyAlone()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(settings, SearchThenAnswer("The Swedish GP is on 3 May."), SearchResults());

        widget.Text = "when is the swedish gp";
        await widget.AskCommand.ExecuteAsync(null);

        Assert.Equal("when is the swedish gp", widget.Text);
        Assert.False(widget.CanUndoSpellCheck);
        Assert.Equal("The Swedish GP is on 3 May.", widget.AnswerText);
        Assert.Equal("when is the swedish gp", widget.AskedQuestion);
        Assert.True(widget.HasAnswer);
        Assert.True(widget.AnswerUsedSearch);
    }

    [Fact]
    public async Task Ask_ShowsTheSourcesBehindTheAnswer()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(settings, SearchThenAnswer("They won 3-1."), SearchResults());

        widget.Text = "who won last night";
        await widget.AskCommand.ExecuteAsync(null);

        QuickAnswerSource source = Assert.Single(widget.AnswerSources);
        Assert.Equal("https://sport.test/report", source.Url);
        Assert.Equal("Sport Test", source.SiteName);
        Assert.Contains("Checked against the web", widget.AnswerOriginText);
    }

    /// <summary>
    /// An answer nothing was checked against has to say so. Without the label the user
    /// cannot tell a sourced answer from a remembered one, which is the whole point.
    /// </summary>
    [Fact]
    public async Task Ask_LabelsAnUncheckedAnswerAsComingFromTheModel()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        settings.QuickAnswerSearchMode = AppSettings.SearchModeNever;
        using var widget = NewWidget(settings, Answer("Six times seven is forty-two."), SearchResults());

        widget.Text = "what is six times seven";
        await widget.AskCommand.ExecuteAsync(null);

        Assert.False(widget.AnswerUsedSearch);
        Assert.Empty(widget.AnswerSources);
        Assert.Contains("not checked against the web", widget.AnswerOriginText);
    }

    /// <summary>
    /// The two actions report into separate lines, so a failed question never wipes the
    /// confirmation from a spell check the user is still reading.
    /// </summary>
    [Fact]
    public async Task Ask_ReportsItsErrorWithoutDisturbingTheSpellCheckStatus()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-bad-key");
        using var widget = NewWidget(
            settings,
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            },
            SearchResults());

        widget.Text = "who won last night";
        widget.StatusText = "Spelling fixed.";

        await widget.AskCommand.ExecuteAsync(null);

        Assert.True(widget.IsAskStatusError);
        Assert.Contains("rejected the API key", widget.AskStatusText);
        Assert.False(widget.HasAnswer);
        Assert.Equal("Spelling fixed.", widget.StatusText);
        Assert.Equal("who won last night", widget.Text);
    }

    /// <summary>
    /// Reading the answer while typing the next question is the normal way to use this,
    /// so an edit keeps the card. AskedQuestion is what stops it reading as a reply to
    /// whatever is in the box now.
    /// </summary>
    [Fact]
    public async Task EditingTheTextKeepsTheAnswerAndTheQuestionItBelongsTo()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(settings, SearchThenAnswer("They won 3-1."), SearchResults());

        widget.Text = "who won last night";
        await widget.AskCommand.ExecuteAsync(null);

        widget.Text = "a completely different sentence";

        Assert.Equal("They won 3-1.", widget.AnswerText);
        Assert.Equal("who won last night", widget.AskedQuestion);
    }

    [Fact]
    public async Task DismissAnswer_ClearsTheCardAndLeavesTheTextBehind()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(settings, SearchThenAnswer("They won 3-1."), SearchResults());

        widget.Text = "who won last night";
        await widget.AskCommand.ExecuteAsync(null);

        widget.DismissAnswerCommand.Execute(null);

        Assert.False(widget.HasAnswer);
        Assert.Empty(widget.AnswerSources);
        Assert.Equal(string.Empty, widget.AskedQuestion);
        Assert.Equal("who won last night", widget.Text);
        Assert.False(widget.DismissAnswerCommand.CanExecute(null));
    }

    [Fact]
    public async Task Clear_EmptiesTheBoxAndTheAnswerTogether()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(settings, SearchThenAnswer("They won 3-1."), SearchResults());

        widget.Text = "who won last night";
        await widget.AskCommand.ExecuteAsync(null);

        widget.ClearCommand.Execute(null);

        Assert.Equal(string.Empty, widget.Text);
        Assert.False(widget.HasAnswer);
        Assert.Empty(widget.AnswerSources);
    }

    /// <summary>An answer left on screen is still worth clearing after the box is empty.</summary>
    [Fact]
    public async Task Clear_StaysAvailableWhileOnlyAnAnswerIsLeft()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(settings, SearchThenAnswer("They won 3-1."), SearchResults());

        widget.Text = "who won last night";
        await widget.AskCommand.ExecuteAsync(null);
        widget.Text = string.Empty;

        Assert.True(widget.ClearCommand.CanExecute(null));
    }

    /// <summary>
    /// The overlay makes room for the first answer only; after that the size is the
    /// user's to keep.
    /// </summary>
    [Fact]
    public async Task AnswerShown_IsRaisedForTheFirstAnswerOnly()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(settings, SearchThenAnswer("An answer."), SearchResults());

        int raised = 0;
        widget.AnswerShown += (_, _) => raised++;

        widget.Text = "first question";
        await widget.AskCommand.ExecuteAsync(null);
        Assert.Equal(1, raised);

        widget.Text = "second question";
        await widget.AskCommand.ExecuteAsync(null);
        Assert.Equal(1, raised);
    }

    /// <summary>
    /// A pasted paragraph is not a question. Ask steps aside rather than billing for it,
    /// but fixing its spelling is still exactly what the box is for.
    /// </summary>
    [Fact]
    public void ATextTooLongToAskCanStillHaveItsSpellingFixed()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(settings, Answer("never reached"), SearchResults());

        widget.Text = new string('a', QuickAnswerService.MaxQuestionLength + 1);

        Assert.True(widget.IsTooLongToAsk);
        Assert.False(widget.AskCommand.CanExecute(null));
        Assert.True(widget.SpellCheckCommand.CanExecute(null));

        widget.Text = "short enough";

        Assert.False(widget.IsTooLongToAsk);
        Assert.True(widget.AskCommand.CanExecute(null));
    }

    [Fact]
    public void SetupNotice_NamesTheMissingKeyWithoutClaimingTheWidgetIsBroken()
    {
        using var directory = new TemporaryDirectory();
        var settings = new AppSettingsService(directory.Path);
        using var widget = new TextHelperWidgetViewModel(settings);

        Assert.Contains("DeepSeek API key", widget.SetupNoticeText);

        Assert.True(settings.TrySetDeepSeekApiKey("sk-test-key", out _));
        widget.Refresh();

        // Spell checking works now; only the web check is missing, and it says so.
        Assert.Contains("Brave Search key", widget.SetupNoticeText);
        Assert.Contains("not checked against the web", widget.SetupNoticeText);

        Assert.True(settings.TrySetBraveApiKey("brave-key", out _));
        widget.Refresh();

        Assert.False(widget.HasSetupNotice);
    }

    [Fact]
    public void SetupNotice_SaysNothingAboutBraveWhenSearchingIsTurnedOff()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        settings.QuickAnswerSearchMode = AppSettings.SearchModeNever;
        using var widget = new TextHelperWidgetViewModel(settings);

        Assert.False(widget.HasSetupNotice);
    }

    [Fact]
    public void TheTwoActionsTakeTurnsRatherThanRunningTogether()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = new TextHelperWidgetViewModel(settings) { Text = "something" };

        Assert.True(widget.SpellCheckCommand.CanExecute(null));
        Assert.True(widget.AskCommand.CanExecute(null));

        widget.IsAsking = true;

        Assert.True(widget.IsWorking);
        Assert.False(widget.SpellCheckCommand.CanExecute(null));
        Assert.False(widget.AskCommand.CanExecute(null));
    }

    /// <summary>
    /// A failed question leaves no answer, so nothing but the status line holds the card
    /// open. Without that the error is written into a panel that collapses in the same
    /// breath, and the button looks like it simply did nothing.
    /// </summary>
    [Fact]
    public async Task AFailedQuestionKeepsItsErrorOnScreen()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(
            settings,
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            },
            SearchResults());

        widget.Text = "who won last night";
        await widget.AskCommand.ExecuteAsync(null);

        Assert.False(widget.HasAnswer);
        Assert.True(widget.IsAnswerAreaVisible);
        Assert.True(widget.IsAskStatusError);
        Assert.NotEqual(string.Empty, widget.AskStatusText);

        // And it can be dismissed, even though there is no answer under it.
        Assert.True(widget.DismissAnswerCommand.CanExecute(null));
        widget.DismissAnswerCommand.Execute(null);
        Assert.False(widget.IsAnswerAreaVisible);
    }

    /// <summary>Typing again is the other way out of a stale error.</summary>
    [Fact]
    public async Task TypingAgainClearsAFailedQuestionsError()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");
        using var widget = NewWidget(
            settings,
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            },
            SearchResults());

        widget.Text = "who won last night";
        await widget.AskCommand.ExecuteAsync(null);
        Assert.True(widget.IsAnswerAreaVisible);

        widget.Text = "who won last night?";

        Assert.Equal(string.Empty, widget.AskStatusText);
        Assert.False(widget.IsAskStatusError);
        Assert.False(widget.IsAnswerAreaVisible);
    }

    /// <summary>
    /// A new question empties the card before it is sent. Leaving the old answer up means
    /// a failure shows the previous answer, the previous question and a fresh error at
    /// once, which reads as though the answer on screen had failed.
    /// </summary>
    [Fact]
    public async Task AFailedQuestionDoesNotLeaveThePreviousAnswerUnderTheError()
    {
        using var directory = new TemporaryDirectory();
        var settings = SettingsWithKey(directory.Path, "sk-test-key");

        int calls = 0;
        Func<HttpRequestMessage, HttpResponseMessage> firstSucceedsThenFails = _ =>
        {
            calls++;
            if (calls <= 2)
                return calls == 1 ? ToolCallResponse() : AnswerResponse("The first answer.");

            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        };

        using var widget = NewWidget(settings, firstSucceedsThenFails, SearchResults());

        widget.Text = "first question";
        await widget.AskCommand.ExecuteAsync(null);
        Assert.Equal("The first answer.", widget.AnswerText);
        Assert.Equal("first question", widget.AskedQuestion);

        widget.Text = "second question";
        await widget.AskCommand.ExecuteAsync(null);

        Assert.False(widget.HasAnswer);
        Assert.Equal(string.Empty, widget.AskedQuestion);
        Assert.Empty(widget.AnswerSources);
        Assert.True(widget.IsAskStatusError);
        Assert.True(widget.IsAnswerAreaVisible);
    }

    private static TextHelperWidgetViewModel NewWidget(
        AppSettingsService settings,
        Func<HttpRequestMessage, HttpResponseMessage> deepSeekHandler,
        Func<HttpRequestMessage, HttpResponseMessage> braveHandler)
    {
        var quickAnswers = new QuickAnswerService(
            new HttpClient(new StubHttpMessageHandler(deepSeekHandler)),
            TestEndpoint,
            new BraveSearchService(new HttpClient(new StubHttpMessageHandler(braveHandler)), BraveTestEndpoint));

        // Both keys: without a Brave key every mode collapses to a model-only answer,
        // which is its own test rather than the default these share.
        Assert.True(settings.TrySetBraveApiKey("brave-key", out _));

        return new TextHelperWidgetViewModel(settings, quickAnswerService: quickAnswers);
    }

    /// <summary>One routing response asking to search.</summary>
    private static HttpResponseMessage ToolCallResponse() =>
        Json("{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"tool_calls\":[{"
            + "\"function\":{\"name\":\"search_the_web\",\"arguments\":\"{\\\"query\\\":\\\"q\\\"}\"}}]}}]}");

    /// <summary>One finished answer.</summary>
    private static HttpResponseMessage AnswerResponse(string answer) =>
        Json("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":"
            + JsonSerializer.Serialize(answer) + "}}]}");

    /// <summary>
    /// A DeepSeek that answers straight away without asking to search.
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Answer(string answer) =>
        _ => AnswerResponse(answer);

    /// <summary>
    /// A DeepSeek that takes the search tool on the routing call and answers on the
    /// second. Alternating keeps it consistent across repeated questions.
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> SearchThenAnswer(string answer)
    {
        int calls = 0;
        return _ =>
        {
            calls++;
            return calls % 2 == 1 ? ToolCallResponse() : AnswerResponse(answer);
        };
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> SearchResults() => _ => Json("""
        {"web":{"results":[{
          "title":"Race report",
          "url":"https://sport.test/report",
          "description":"They won 3-1.",
          "profile":{"name":"Sport Test"}
        }]}}
        """);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private const string BraveTestEndpoint = "https://api.search.brave.test/res/v1/web/search";

    private const string TestEndpoint = "https://api.deepseek.test/chat/completions";

    private static AppSettingsService SettingsWithKey(string path, string key)
    {
        var settings = new AppSettingsService(path);
        Assert.True(settings.TrySetDeepSeekApiKey(key, out _));
        return settings;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The clipboard test timed out.");

        if (failure != null)
            throw failure;
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
