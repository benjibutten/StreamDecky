using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Helpers;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

/// <summary>
/// A stored submission shown in the overlay history: per-field copy buttons with
/// done-checkmarks so the user can work through a multi-field in-game form.
/// Copying a field checks it automatically; when every field has been copied the
/// whole submission is marked completed (persisted).
/// </summary>
public partial class OverlayFormSubmissionViewModel : ObservableObject
{
    private readonly Func<OverlayFormSubmissionViewModel, bool, bool> _onCompletedChanged;
    private readonly Func<OverlayFormSubmissionViewModel, string, string, bool> _onFieldValueChanged;

    public OverlayFormSubmissionViewModel(
        FormSubmission model,
        Func<OverlayFormSubmissionViewModel, bool, bool> onCompletedChanged,
        Func<OverlayFormSubmissionViewModel, string, string, bool>? onFieldValueChanged = null,
        Func<string, bool>? canEditField = null)
    {
        Model = model;
        _onCompletedChanged = onCompletedChanged;
        _onFieldValueChanged = onFieldValueChanged ?? ((_, _, _) => false);
        _isCompleted = model.IsCompleted;

        FieldValues = new ObservableCollection<OverlayFormSubmissionFieldViewModel>(model.Values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new OverlayFormSubmissionFieldViewModel(
                pair.Key,
                pair.Value,
                canEditField?.Invoke(pair.Key) == true,
                OnFieldCopied,
                OnFieldValueChanged)));
    }

    public FormSubmission Model { get; }

    public string Id => Model.Id;
    public string TemplateName => string.IsNullOrWhiteSpace(Model.TemplateName) ? "(unnamed form)" : Model.TemplateName;
    public string CreatedText => Model.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm");
    public string RenderedText => Model.RenderedText;
    public ObservableCollection<OverlayFormSubmissionFieldViewModel> FieldValues { get; }
    public bool HasFieldValues => FieldValues.Count > 0;

    [ObservableProperty]
    private bool _isCompleted;

    partial void OnIsCompletedChanged(bool value)
    {
        if (!_onCompletedChanged(this, value))
        {
            _isCompleted = Model.IsCompleted;
            OnPropertyChanged(nameof(IsCompleted));
            return;
        }

        Model.IsCompleted = value;

        if (!value)
        {
            foreach (var field in FieldValues)
                field.ResetCopied();
        }

    }

    [RelayCommand]
    private void CopyAll()
    {
        if (string.IsNullOrWhiteSpace(RenderedText))
            return;

        ClipboardHelper.TrySetText(RenderedText);
    }

    private void OnFieldCopied()
    {
        if (!IsCompleted && FieldValues.All(field => field.IsCopied))
            IsCompleted = true;
    }

    private bool OnFieldValueChanged(OverlayFormSubmissionFieldViewModel field, string value)
    {
        if (!_onFieldValueChanged(this, field.Label, value))
            return false;

        OnPropertyChanged(nameof(RenderedText));
        return true;
    }
}

public partial class OverlayFormSubmissionFieldViewModel : ObservableObject
{
    private readonly Action _onCopied;
    private readonly Func<OverlayFormSubmissionFieldViewModel, string, bool> _onValueChanged;
    private readonly Func<string, bool> _trySetClipboardText;

    public OverlayFormSubmissionFieldViewModel(
        string label,
        string value,
        bool canEdit,
        Action onCopied,
        Func<OverlayFormSubmissionFieldViewModel, string, bool> onValueChanged,
        Func<string, bool>? trySetClipboardText = null)
    {
        Label = label;
        _value = value;
        _editValue = value;
        CanEdit = canEdit;
        _onCopied = onCopied;
        _onValueChanged = onValueChanged;
        _trySetClipboardText = trySetClipboardText ?? ClipboardHelper.TrySetText;
    }

    public string Label { get; }
    public string LabelText => $"{Label}:";
    public bool CanEdit { get; }

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private string _editValue;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isCopied;

    [RelayCommand]
    public void BeginEdit()
    {
        if (!CanEdit)
            return;

        EditValue = Value;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        if (!IsEditing)
            return;

        string updatedValue = EditValue ?? string.Empty;
        IsEditing = false;
        if (string.Equals(Value, updatedValue, StringComparison.Ordinal))
            return;

        if (_onValueChanged(this, updatedValue))
        {
            Value = updatedValue;
            IsCopied = false;
        }
        else
        {
            EditValue = Value;
        }
    }

    public void CancelEdit()
    {
        if (!IsEditing)
            return;

        EditValue = Value;
        IsEditing = false;
    }

    public void ResetCopied()
    {
        IsCopied = false;
    }

    [RelayCommand]
    private void Copy()
    {
        if (!_trySetClipboardText(Value))
            return;

        IsCopied = true;
        _onCopied();
    }
}
