using StreamDecky.Models;
using StreamDecky.Services;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class MainViewModelActionExecutionTests
{
    [Fact]
    public async Task ExecuteButton_TextInput_ClosesOverlayAndRoutesToTextService()
    {
        using var tempDirectory = new TemporaryDirectory();
        var textService = new RecordingTextInputActionService();
        var multiActionService = new RecordingMultiActionService();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path), textService, multiActionService);
        ButtonViewModel button = viewModel.Buttons[0];

        button.ActionType = ActionType.TextInput;
        button.Text = "hello";
        viewModel.IsOverlayOpen = true;

        await viewModel.ExecuteButtonCommand.ExecuteAsync(button);

        Assert.False(viewModel.IsOverlayOpen);
        Assert.Equal(1, textService.CallCount);
        Assert.Same(button.Config, textService.LastConfig);
        Assert.Equal(0, multiActionService.KeyPressCallCount);
        Assert.Equal(0, multiActionService.AsyncCallCount);
    }

    [Fact]
    public async Task ExecuteButton_KeyPress_ClosesOverlayAndRoutesToMultiActionService()
    {
        using var tempDirectory = new TemporaryDirectory();
        var textService = new RecordingTextInputActionService();
        var multiActionService = new RecordingMultiActionService();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path), textService, multiActionService);
        ButtonViewModel button = viewModel.Buttons[0];

        button.ActionType = ActionType.KeyPress;
        button.KeyText = "CTRL+SHIFT+P";
        viewModel.IsOverlayOpen = true;

        await viewModel.ExecuteButtonCommand.ExecuteAsync(button);

        Assert.False(viewModel.IsOverlayOpen);
        Assert.Equal(1, multiActionService.KeyPressCallCount);
        Assert.Equal("CTRL+SHIFT+P", multiActionService.LastKeyText);
        Assert.Equal(0, textService.CallCount);
    }

    [Fact]
    public async Task ExecuteButton_MultiAction_ClosesOverlayAndRoutesToAsyncExecutor()
    {
        using var tempDirectory = new TemporaryDirectory();
        var textService = new RecordingTextInputActionService();
        var multiActionService = new RecordingMultiActionService();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path), textService, multiActionService);
        ButtonViewModel button = viewModel.Buttons[0];

        button.ActionType = ActionType.MultiAction;
        button.Steps.Add(new ActionStep { Type = ActionStepType.Delay, DelayMs = 10 });
        viewModel.IsOverlayOpen = true;

        await viewModel.ExecuteButtonCommand.ExecuteAsync(button);

        Assert.False(viewModel.IsOverlayOpen);
        Assert.Equal(1, multiActionService.AsyncCallCount);
        Assert.Same(button.Config, multiActionService.LastAsyncConfig);
        Assert.Equal(0, textService.CallCount);
    }

    [Fact]
    public void ExecuteQuickTextAction_RoutesItemTextThroughMultiActionService()
    {
        using var tempDirectory = new TemporaryDirectory();
        var textService = new RecordingTextInputActionService();
        var multiActionService = new RecordingMultiActionService();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path), textService, multiActionService);

        viewModel.QuickTextActionSteps.Add(new ActionStep { Type = ActionStepType.TextInput, Text = "{{itemText}}" });

        viewModel.ExecuteQuickTextAction("macro text");

        Assert.Equal(1, multiActionService.ExecuteWithItemTextCallCount);
        Assert.Equal("macro text", multiActionService.LastItemText);
    }

    private sealed class RecordingTextInputActionService : TextInputActionService
    {
        public int CallCount { get; private set; }

        public ButtonConfig? LastConfig { get; private set; }

        public override void Execute(ButtonConfig config, bool useNaturalTyping = false)
        {
            CallCount++;
            LastConfig = config;
        }
    }

    private sealed class RecordingMultiActionService : MultiActionService
    {
        public int KeyPressCallCount { get; private set; }

        public int AsyncCallCount { get; private set; }

        public int ExecuteWithItemTextCallCount { get; private set; }

        public string? LastKeyText { get; private set; }

        public string? LastItemText { get; private set; }

        public ButtonConfig? LastAsyncConfig { get; private set; }

        public override Task ExecuteAsync(ButtonConfig config, bool useNaturalTyping = false)
        {
            AsyncCallCount++;
            LastAsyncConfig = config;
            return Task.CompletedTask;
        }

        public override void ExecuteKeyPress(string keyText)
        {
            KeyPressCallCount++;
            LastKeyText = keyText;
        }

        public override void ExecuteWithItemText(IEnumerable<ActionStep> steps, string itemText, bool useNaturalTyping = false)
        {
            ExecuteWithItemTextCallCount++;
            LastItemText = itemText;
        }
    }
}