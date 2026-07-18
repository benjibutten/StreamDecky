using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

/// <summary>
/// One field being filled in inside the overlay Forms panel.
///
/// The field keeps a hidden pattern (its default value, chosen option, or the
/// user's last edit) and always displays that pattern with tokens expanded, so
/// "Payment from {who}" shows the who-field's value live and re-expands whenever
/// the referenced field changes. A manual edit takes over as the new pattern, so
/// handcrafted text never mutates under the user's cursor.
/// </summary>
public partial class FormFieldSessionViewModel : ObservableObject
{
    public const int MaxVisibleSuggestions = 3;

    private readonly Action _onValueChanged;
    private readonly Func<FormFieldSessionViewModel, string, string> _expandPattern;
    private readonly IReadOnlyList<string> _historyValues;
    private string _pattern = string.Empty;
    private bool _isRefreshingFromPattern;

    public FormFieldSessionViewModel(
        FormField field,
        IReadOnlyList<string> historyValues,
        Func<FormFieldSessionViewModel, string, string> expandPattern,
        Action onValueChanged)
    {
        Field = field;
        _historyValues = historyValues;
        _expandPattern = expandPattern;
        _onValueChanged = onValueChanged;

        Reset();
    }

    public FormField Field { get; }

    public string Label => Field.DisplayLabel;
    public bool IsChoice => Field.Type == FormFieldType.Choice;
    public bool IsMultiline => Field.IsMultiline;
    public ObservableCollection<FormFieldOption> Options => Field.Options;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private FormFieldOption? _selectedOption;

    public ObservableCollection<string> Suggestions { get; } = new();

    public bool HasSuggestions => Suggestions.Count > 0;

    partial void OnValueChanged(string value)
    {
        UpdateSuggestions();

        if (_isRefreshingFromPattern)
            return;

        // A user edit becomes the new pattern; any tokens they typed stay live.
        _pattern = value ?? string.Empty;
        _onValueChanged();
    }

    [RelayCommand]
    private void ApplySuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
            return;

        Value = suggestion;
    }

    [RelayCommand]
    private void ApplyOption(FormFieldOption? option)
    {
        if (option == null)
            return;

        SelectedOption = option;
        SetPattern(option.Text);
    }

    /// <summary>Restores the default value, e.g. after a submission.</summary>
    public void Reset()
    {
        SelectedOption = null;
        SetPattern(Field.DefaultValue);
        UpdateSuggestions();
    }

    public void SetPattern(string? pattern)
    {
        _pattern = pattern ?? string.Empty;
        RefreshFromPattern();
        _onValueChanged();
    }

    /// <summary>Re-expands the displayed value from the pattern, e.g. after a
    /// referenced field changed. Does not notify the parent; the caller decides
    /// when to refresh the preview.</summary>
    public void RefreshFromPattern()
    {
        string expanded = _expandPattern(this, _pattern);
        if (string.Equals(expanded, Value, StringComparison.Ordinal))
            return;

        _isRefreshingFromPattern = true;
        try
        {
            Value = expanded;
        }
        finally
        {
            _isRefreshingFromPattern = false;
        }
    }

    private void UpdateSuggestions()
    {
        Suggestions.Clear();

        if (Field.RememberHistory)
        {
            string query = Value?.Trim() ?? string.Empty;
            IEnumerable<string> matches = string.IsNullOrEmpty(query)
                ? _historyValues
                : _historyValues
                    .Where(entry => entry.Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(entry, query, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => entry.StartsWith(query, StringComparison.OrdinalIgnoreCase));

            foreach (var match in matches.Take(MaxVisibleSuggestions))
                Suggestions.Add(match);
        }

        OnPropertyChanged(nameof(HasSuggestions));
    }
}
