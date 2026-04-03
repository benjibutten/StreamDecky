using System.Windows;
using StreamDecky.Helpers;
using StreamDecky.Models;

using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace StreamDecky.Services;

public class TextInputActionService
{
    public void Execute(ButtonConfig config, bool useNaturalTyping = false)
    {
        if (string.IsNullOrEmpty(config.Text))
            return;

        switch (config.TextMode)
        {
            case TextMode.PasteFromClipboard:
                ExecutePaste(config.Text, config.PressEnterAfter);
                break;
            case TextMode.SimulateTyping:
                ExecuteSimulateTyping(config.Text, config.PressEnterAfter, useNaturalTyping);
                break;
        }
    }

    private void ExecutePaste(string text, bool pressEnter)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Clipboard.SetText(text);
        });

        RunDetached(async () =>
        {
            await Task.Delay(500);
            await InputSimulator.SendPasteAsync();
            if (pressEnter)
            {
                await Task.Delay(50);
                await InputSimulator.SendEnterAsync();
            }
        });
    }

    private void ExecuteSimulateTyping(string text, bool pressEnter, bool useNaturalTyping)
    {
        RunDetached(async () =>
        {
            await Task.Delay(500);
            await InputSimulator.SendTextAsync(text, useNaturalCadence: useNaturalTyping);
            if (pressEnter)
            {
                await Task.Delay(50);
                await InputSimulator.SendEnterAsync();
            }
        });
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
                // Ignore background execution errors to avoid surfacing transient input failures to UI.
            }
        });
    }
}
