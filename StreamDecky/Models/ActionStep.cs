using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDecky.Models;

public partial class ActionStep : ObservableObject
{
    [ObservableProperty]
    private ActionStepType _type = ActionStepType.KeyPress;

    [ObservableProperty]
    private string _keyText = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private TextMode _textMode = TextMode.SimulateTyping;

    [ObservableProperty]
    private bool _pressEnterAfter;

    [ObservableProperty]
    private int _delayMs = 100;
}
