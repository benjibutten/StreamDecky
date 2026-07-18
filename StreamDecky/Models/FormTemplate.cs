using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDecky.Models;

public partial class FormTemplate : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = "New form";

    /// <summary>Free text composing the final output. Supports {fieldKey},
    /// {counterName}, and built-in tokens such as {date} and {time}.</summary>
    [ObservableProperty]
    private string _outputTemplate = string.Empty;

    /// <summary>Opt-in: shows a Copy button next to Save in the overlay. Off by
    /// default so Copy cannot be hit by mistake when the user means to Save.</summary>
    [ObservableProperty]
    private bool _showCopyButton;

    public ObservableCollection<FormField> Fields { get; set; } = new();

    public ObservableCollection<FormCounter> Counters { get; set; } = new();

    /// <summary>Optional action pipeline for sending the rendered text to the
    /// previously focused window. Runs through the same step engine as the
    /// clipboard list; the rendered text is injected as the item text.</summary>
    public ObservableCollection<ActionStep> ActionSteps { get; set; } = new();

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(Name))
            Name = "New form";

        OutputTemplate ??= string.Empty;
        Fields ??= new ObservableCollection<FormField>();
        Counters ??= new ObservableCollection<FormCounter>();
        ActionSteps ??= new ObservableCollection<ActionStep>();

        foreach (var field in Fields)
            field.EnsureInitialized();

        foreach (var counter in Counters)
            counter.EnsureInitialized();
    }
}
