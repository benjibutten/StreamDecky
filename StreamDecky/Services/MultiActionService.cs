using System.Windows;
using StreamDecky.Helpers;
using StreamDecky.Models;

using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace StreamDecky.Services;

public class MultiActionService
{
    public const string ClipboardTextToken = "{{clipboardText}}";

    private static readonly string[] ClipboardTextTokens =
    {
        ClipboardTextToken,
        "{{clipboard}}",
        "{{itemText}}"
    };

    public async Task ExecuteAsync(ButtonConfig config, bool useNaturalTyping = false)
    {
        await ExecuteStepsAsync(config.Steps, itemText: null, useNaturalTyping);
    }

    /// <summary>
    /// Executes a list of action steps on behalf of a clipboard item. For TextInput steps with
    /// empty Text, the provided <paramref name="itemText"/> is injected automatically.
    /// </summary>
    public void ExecuteWithItemText(IEnumerable<ActionStep> steps, string itemText, bool useNaturalTyping = false)
    {
        RunDetached(() => ExecuteStepsAsync(steps, itemText, useNaturalTyping));
    }

    public async Task ExecuteKeyPressAsync(string keyText)
    {
        if (string.IsNullOrEmpty(keyText)) return;

        await Task.Delay(500);
        await InputSimulator.SendKeyPressAsync(keyText);
    }

    /// <summary>
    /// Synchronous fire-and-forget wrapper for button clicks.
    /// </summary>
    public void ExecuteKeyPress(string keyText)
    {
        if (string.IsNullOrEmpty(keyText)) return;

        RunDetached(async () =>
        {
            await Task.Delay(500);
            await InputSimulator.SendKeyPressAsync(keyText);
        });
    }

    private static async Task ExecuteStepsAsync(IEnumerable<ActionStep> steps, string? itemText, bool useNaturalTyping)
    {
        // Longer delay for focus transfer to the target window.
        await Task.Delay(500);

        foreach (var step in steps)
        {
            switch (step.Type)
            {
                case ActionStepType.Delay:
                    await Task.Delay(Math.Max(1, step.DelayMs));
                    break;
                case ActionStepType.KeyPress:
                    if (!string.IsNullOrEmpty(step.KeyText))
                        await InputSimulator.SendKeyPressAsync(step.KeyText);
                    break;
                case ActionStepType.TextInput:
                    await ExecuteResolvedTextStepAsync(step, itemText, useNaturalTyping);
                    break;
            }
        }
    }

    private static async Task ExecuteResolvedTextStepAsync(ActionStep step, string? itemText, bool useNaturalTyping)
    {
        string resolvedText = ResolveClipboardTextTemplate(step.Text, itemText);
        if (string.IsNullOrEmpty(resolvedText))
            return;

        var effective = new ActionStep
        {
            Type = step.Type,
            KeyText = step.KeyText,
            Text = resolvedText,
            TextMode = step.TextMode,
            PressEnterAfter = step.PressEnterAfter,
            DelayMs = step.DelayMs,
        };

        await ExecuteTextStepAsync(effective, useNaturalTyping);
    }

    private static string ResolveClipboardTextTemplate(string template, string? itemText)
    {
        string textValue = itemText ?? string.Empty;
        if (string.IsNullOrEmpty(template))
            return textValue;

        string resolved = template;
        foreach (string token in ClipboardTextTokens)
            resolved = resolved.Replace(token, textValue, StringComparison.Ordinal);

        return resolved;
    }

    private static async Task ExecuteTextStepAsync(ActionStep step, bool useNaturalTyping)
    {
        if (string.IsNullOrEmpty(step.Text)) return;

        if (step.TextMode == TextMode.PasteFromClipboard)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Clipboard.SetText(step.Text);
            });
            await InputSimulator.SendPasteAsync();
        }
        else
        {
            await InputSimulator.SendTextAsync(step.Text, useNaturalCadence: useNaturalTyping);
        }

        if (step.PressEnterAfter)
            await InputSimulator.SendEnterAsync();
    }

    private static void RunDetached(Func<Task> operation)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch
            {
                // Ignore background execution errors to avoid breaking user interaction flow.
            }
        });
    }
}
