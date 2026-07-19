using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamDecky.Models;

public partial class FormField : ObservableObject
{
    public const string SharedHistoryPrefix = "shared:";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Token name used as {Key} in the output template. Shares one
    /// namespace with counter names and built-in tokens.</summary>
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private FormFieldType _type = FormFieldType.Text;

    /// <summary>Prefilled value when the form opens. May contain counter and
    /// built-in tokens, which are expanded when the overlay session starts.</summary>
    [ObservableProperty]
    private string _defaultValue = string.Empty;

    [ObservableProperty]
    private bool _isMultiline;

    /// <summary>Prevents the form from being submitted while this field is empty.</summary>
    [ObservableProperty]
    private bool _isRequired;

    /// <summary>When enabled, previously submitted values for this field are
    /// stored and offered as suggestions in the overlay.</summary>
    [ObservableProperty]
    private bool _rememberHistory;

    /// <summary>Optional opt-in namespace used to share only autocomplete values
    /// with fields in other form templates that use the same key.</summary>
    [ObservableProperty]
    private string _sharedSuggestionKey = string.Empty;

    /// <summary>Allows this field's stored value to be corrected directly from
    /// the overlay history. The current template controls editability; the
    /// submission data itself remains profile-independent.</summary>
    [ObservableProperty]
    private bool _allowHistoryEditing;

    public ObservableCollection<FormFieldOption> Options { get; set; } = new();

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Key : Label;

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        Key = NormalizeKey(Key);
        Label ??= string.Empty;
        DefaultValue ??= string.Empty;
        SharedSuggestionKey = NormalizeKey(SharedSuggestionKey);
        Options ??= new ObservableCollection<FormFieldOption>();

        foreach (var option in Options)
            option.EnsureInitialized();
    }

    public static string NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var kept = key.Trim()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')
            .ToArray();
        return new string(kept);
    }

    public string GetSuggestionHistoryKey()
    {
        string sharedKey = NormalizeKey(SharedSuggestionKey);
        return string.IsNullOrWhiteSpace(sharedKey)
            ? Id
            : SharedHistoryPrefix + sharedKey.ToLowerInvariant();
    }
}
