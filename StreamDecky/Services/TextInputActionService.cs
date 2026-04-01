using System.Windows;
using StreamDecky.Helpers;
using StreamDecky.Models;

using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace StreamDecky.Services;

public class TextInputActionService
{
    public void Execute(ButtonConfig config)
    {
        if (string.IsNullOrEmpty(config.Text))
            return;

        switch (config.TextMode)
        {
            case TextMode.PasteFromClipboard:
                ExecutePaste(config.Text, config.PressEnterAfter);
                break;
            case TextMode.SimulateTyping:
                ExecuteSimulateTyping(config.Text, config.PressEnterAfter);
                break;
        }
    }

    private void ExecutePaste(string text, bool pressEnter)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Clipboard.SetText(text);
        });

        Task.Run(async () =>
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

    private void ExecuteSimulateTyping(string text, bool pressEnter)
    {
        Task.Run(async () =>
        {
            await Task.Delay(500);
            InputSimulator.SendText(text);
            if (pressEnter)
            {
                await Task.Delay(50);
                await InputSimulator.SendEnterAsync();
            }
        });
    }
}
