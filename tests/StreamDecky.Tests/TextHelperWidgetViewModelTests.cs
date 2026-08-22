using System.Net;
using System.Text;
using System.Text.Json;
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
