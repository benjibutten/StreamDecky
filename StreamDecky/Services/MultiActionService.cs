using System.Windows;
using StreamDecky.Helpers;
using StreamDecky.Models;

using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace StreamDecky.Services;

public class MultiActionService
{
    public async Task ExecuteAsync(ButtonConfig config)
    {
        // Longer delay for focus transfer to game window
        await Task.Delay(500);

        foreach (var step in config.Steps)
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
                    await ExecuteTextStepAsync(step);
                    break;
            }
        }
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

        Task.Run(async () =>
        {
            await Task.Delay(500);
            await InputSimulator.SendKeyPressAsync(keyText);
        });
    }

    private static async Task ExecuteTextStepAsync(ActionStep step)
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
            InputSimulator.SendText(step.Text);
        }

        if (step.PressEnterAfter)
            await InputSimulator.SendEnterAsync();
    }
}
