using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;
using StreamDecky.Services;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    private readonly List<FormTemplate> _trackedFormTemplates = new();
    private bool _isLoadingFormTemplates;

    [ObservableProperty]
    private ObservableCollection<FormTemplate> _formTemplates = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFormTemplate))]
    private FormTemplate? _selectedFormTemplate;

    /// <summary>The form active in the overlay panel. Deliberately independent of
    /// the editor selection so browsing forms in the overlay never disturbs an
    /// editing session, and vice versa. Persisted per profile.</summary>
    [ObservableProperty]
    private FormTemplate? _overlayFormTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormFillViewVisible))]
    private bool _isFormHistoryVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenOverlayFormSubmissionCount))]
    [NotifyPropertyChangedFor(nameof(CompletedOverlayFormSubmissionCount))]
    private ObservableCollection<OverlayFormSubmissionViewModel> _overlayFormSubmissions = new();

    [ObservableProperty]
    private ObservableCollection<FormFieldSessionViewModel> _formSessionFields = new();

    [ObservableProperty]
    private string _formPreviewText = string.Empty;

    [ObservableProperty]
    private string _formEditorPreviewText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormTemplateWarning))]
    private string _formTemplateWarningText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormSessionValidationError))]
    private string _formSessionValidationText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _formTokenSuggestions = new();

    [ObservableProperty]
    private ObservableCollection<FormSubmissionViewModel> _formSubmissions = new();

    public bool HasSelectedFormTemplate => SelectedFormTemplate != null;
    public bool HasFormTemplates => FormTemplates.Count > 0;
    public bool HasFormTemplateWarning => !string.IsNullOrWhiteSpace(FormTemplateWarningText);
    public bool HasFormSessionValidationError => !string.IsNullOrWhiteSpace(FormSessionValidationText);
    public bool HasFormSendAction => OverlayFormTemplate is { ActionSteps.Count: > 0 };
    public bool HasFormSubmissions => FormSubmissions.Count > 0;
    public bool HasOverlayFormSubmissions => OverlayFormSubmissions.Count > 0;
    public int OpenOverlayFormSubmissionCount => GetOverlayFormSubmissionsForCount().Count(submission => !submission.IsCompleted);
    public int CompletedOverlayFormSubmissionCount => GetOverlayFormSubmissionsForCount().Count(submission => submission.IsCompleted);
    public string OverlayFormSubmissionCountScopeText => FormsHistoryCountsTodayOnly ? "Today" : "All";
    public bool IsFormFillViewVisible => HasFormTemplates && !IsFormHistoryVisible;

    public bool FormsHistoryCountsTodayOnly
    {
        get => _profile.FormsHistoryCountsTodayOnly;
        set
        {
            if (_profile.FormsHistoryCountsTodayOnly == value)
                return;

            _profile.FormsHistoryCountsTodayOnly = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OverlayFormSubmissionCountScopeText));
            NotifyOverlayFormSubmissionCountsChanged();
            ScheduleAutoSave();
        }
    }

    public bool FormsPanelVisible
    {
        get => _profile.FormsPanelVisible;
        set
        {
            if (_profile.FormsPanelVisible == value)
                return;

            _profile.FormsPanelVisible = value;
            OnPropertyChanged();
            ScheduleAutoSave();

            if (value)
                RefreshFormSession();
        }
    }

    public double FormsPanelX
    {
        get => _profile.FormsPanelX;
        set
        {
            double clamped = Math.Max(0, value);
            if (Math.Abs(_profile.FormsPanelX - clamped) < 0.001)
                return;

            _profile.FormsPanelX = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double FormsPanelY
    {
        get => _profile.FormsPanelY;
        set
        {
            double clamped = Math.Max(0, value);
            if (Math.Abs(_profile.FormsPanelY - clamped) < 0.001)
                return;

            _profile.FormsPanelY = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double FormsPanelWidth
    {
        get => _profile.FormsPanelWidth;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinFormsPanelWidth, DeckProfile.MaxFormsPanelWidth);
            if (Math.Abs(_profile.FormsPanelWidth - clamped) < 0.001)
                return;

            _profile.FormsPanelWidth = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double FormsPanelHeight
    {
        get => _profile.FormsPanelHeight;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinFormsPanelHeight, DeckProfile.MaxFormsPanelHeight);
            if (Math.Abs(_profile.FormsPanelHeight - clamped) < 0.001)
                return;

            _profile.FormsPanelHeight = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    [RelayCommand]
    private void ToggleFormsPanel()
    {
        FormsPanelVisible = !FormsPanelVisible;
    }

    [RelayCommand]
    private void HideFormsPanel()
    {
        FormsPanelVisible = false;
    }

    partial void OnIsOverlayOpenChanged(bool value)
    {
        if (!value)
            return;

        // Recalculate today-scoped counters whenever the overlay is reopened,
        // including when the calendar date changed while the app stayed open.
        NotifyOverlayFormSubmissionCountsChanged();
        if (FormsPanelVisible)
            RefreshFormSession();
    }

    partial void OnSelectedFormTemplateChanged(FormTemplate? value)
    {
        RefreshFormEditorDerivedState();
    }

    partial void OnOverlayFormTemplateChanged(FormTemplate? value)
    {
        if (!_isLoadingFormTemplates)
        {
            string targetId = value?.Id ?? string.Empty;
            if (!string.Equals(_profile.ActiveFormTemplateId, targetId, StringComparison.Ordinal))
            {
                _profile.ActiveFormTemplateId = targetId;
                ScheduleAutoSave();
            }
        }

        RefreshFormSession();
        OnPropertyChanged(nameof(HasFormSendAction));
    }

    partial void OnIsFormHistoryVisibleChanged(bool value)
    {
        if (value)
            LoadFormSubmissions();
    }

    private void LoadFormTemplates()
    {
        _isLoadingFormTemplates = true;
        try
        {
            DetachFormTemplateHandlers();

            foreach (var template in _profile.FormTemplates)
                template.EnsureInitialized();

            FormTemplates = new ObservableCollection<FormTemplate>(_profile.FormTemplates);

            foreach (var template in FormTemplates)
                AttachFormTemplateHandlers(template);

            OverlayFormTemplate = FormTemplates.FirstOrDefault(template =>
                    string.Equals(template.Id, _profile.ActiveFormTemplateId, StringComparison.Ordinal))
                ?? FormTemplates.FirstOrDefault();
            SelectedFormTemplate = OverlayFormTemplate ?? FormTemplates.FirstOrDefault();
        }
        finally
        {
            _isLoadingFormTemplates = false;
        }

        RefreshFormEditorDerivedState();
        RefreshFormSession();
        LoadFormSubmissions();
        NotifyFormTemplateCountChanged();
        OnPropertyChanged(nameof(FormsPanelVisible));
        OnPropertyChanged(nameof(FormsPanelX));
        OnPropertyChanged(nameof(FormsPanelY));
        OnPropertyChanged(nameof(FormsPanelWidth));
        OnPropertyChanged(nameof(FormsPanelHeight));
        OnPropertyChanged(nameof(FormsHistoryCountsTodayOnly));
        OnPropertyChanged(nameof(OverlayFormSubmissionCountScopeText));
    }

    #region Editor commands

    private void NotifyFormTemplateCountChanged()
    {
        OnPropertyChanged(nameof(HasFormTemplates));
        OnPropertyChanged(nameof(IsFormFillViewVisible));
    }

    private string CreateUniqueFormTemplateName(string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Form" : baseName.Trim();
        if (_profile.FormTemplates.All(template =>
            !string.Equals(template.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        int suffix = 2;
        while (_profile.FormTemplates.Any(template =>
            string.Equals(template.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
            suffix++;

        return $"{baseName} {suffix}";
    }

    private string CreateUniqueFormFieldKey(FormTemplate template)
    {
        int suffix = template.Fields.Count + 1;
        string key = $"field{suffix}";
        while (template.Fields.Any(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
            || template.Counters.Any(counter => string.Equals(counter.Name, key, StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
            key = $"field{suffix}";
        }

        return key;
    }

    [RelayCommand]
    private void AddFormTemplate()
    {
        var template = new FormTemplate
        {
            Name = CreateUniqueFormTemplateName($"Form {_profile.FormTemplates.Count + 1}")
        };
        template.EnsureInitialized();

        _profile.FormTemplates.Add(template);
        FormTemplates.Add(template);
        AttachFormTemplateHandlers(template);
        SelectedFormTemplate = template;
        OverlayFormTemplate ??= template;

        NotifyFormTemplateCountChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveFormTemplate()
    {
        var template = SelectedFormTemplate;
        if (template == null)
            return;

        bool confirmed = Helpers.ConfirmDialog.Show(
            System.Windows.Application.Current?.MainWindow,
            "Delete form",
            $"Delete the form \"{template.Name}\"?\n\nStored submissions are kept in the history.",
            confirmText: "Delete",
            danger: true);
        if (!confirmed)
            return;

        int removeIndex = FormTemplates.IndexOf(template);
        DetachFormTemplateHandlers(template);
        _profile.FormTemplates.Remove(template);
        FormTemplates.Remove(template);

        var fallback = FormTemplates.Count > 0
            ? FormTemplates[Math.Clamp(removeIndex, 0, FormTemplates.Count - 1)]
            : null;
        SelectedFormTemplate = fallback;
        if (ReferenceEquals(OverlayFormTemplate, template) || OverlayFormTemplate == null)
            OverlayFormTemplate = fallback;

        NotifyFormTemplateCountChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void DuplicateFormTemplate()
    {
        var source = SelectedFormTemplate;
        if (source == null)
            return;

        var clone = new FormTemplate
        {
            Name = CreateUniqueFormTemplateName(source.Name),
            OutputTemplate = source.OutputTemplate,
            ShowCopyButton = source.ShowCopyButton
        };

        foreach (var field in source.Fields)
        {
            var fieldClone = new FormField
            {
                Key = field.Key,
                Label = field.Label,
                Type = field.Type,
                DefaultValue = field.DefaultValue,
                IsMultiline = field.IsMultiline,
                RememberHistory = field.RememberHistory,
                AllowHistoryEditing = field.AllowHistoryEditing
            };
            foreach (var option in field.Options)
                fieldClone.Options.Add(new FormFieldOption { Label = option.Label, Text = option.Text });
            clone.Fields.Add(fieldClone);
        }

        foreach (var counter in source.Counters)
            clone.Counters.Add(new FormCounter
            {
                Name = counter.Name,
                NextValue = counter.NextValue,
                PadWidth = counter.PadWidth
            });

        foreach (var step in source.ActionSteps)
            clone.ActionSteps.Add(new ActionStep
            {
                Type = step.Type,
                KeyText = step.KeyText,
                Text = step.Text,
                TextMode = step.TextMode,
                PressEnterAfter = step.PressEnterAfter,
                DelayMs = step.DelayMs
            });

        clone.EnsureInitialized();
        _profile.FormTemplates.Add(clone);
        FormTemplates.Add(clone);
        AttachFormTemplateHandlers(clone);
        SelectedFormTemplate = clone;

        NotifyFormTemplateCountChanged();
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void AddFormField()
    {
        var template = SelectedFormTemplate;
        if (template == null)
            return;

        var field = new FormField
        {
            Key = CreateUniqueFormFieldKey(template)
        };
        field.EnsureInitialized();
        template.Fields.Add(field);
    }

    [RelayCommand]
    private void RemoveFormField(FormField? field)
    {
        SelectedFormTemplate?.Fields.Remove(field!);
    }

    [RelayCommand]
    private void MoveFormFieldUp(FormField? field)
    {
        var template = SelectedFormTemplate;
        if (template == null || field == null)
            return;

        int index = template.Fields.IndexOf(field);
        if (index > 0)
            template.Fields.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveFormFieldDown(FormField? field)
    {
        var template = SelectedFormTemplate;
        if (template == null || field == null)
            return;

        int index = template.Fields.IndexOf(field);
        if (index >= 0 && index < template.Fields.Count - 1)
            template.Fields.Move(index, index + 1);
    }

    [RelayCommand]
    private void AddFormFieldOption(FormField? field)
    {
        if (field == null)
            return;

        var option = new FormFieldOption
        {
            Label = $"Option {field.Options.Count + 1}"
        };
        option.EnsureInitialized();
        field.Options.Add(option);
    }

    [RelayCommand]
    private void RemoveFormFieldOption(FormFieldOption? option)
    {
        if (option == null)
            return;

        var field = SelectedFormTemplate?.Fields.FirstOrDefault(candidate => candidate.Options.Contains(option));
        field?.Options.Remove(option);
    }

    [RelayCommand]
    private void AddFormCounter()
    {
        var template = SelectedFormTemplate;
        if (template == null)
            return;

        int suffix = template.Counters.Count + 1;
        string name = $"counter{suffix}";
        while (template.Counters.Any(counter => string.Equals(counter.Name, name, StringComparison.OrdinalIgnoreCase))
            || template.Fields.Any(field => string.Equals(field.Key, name, StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
            name = $"counter{suffix}";
        }

        var counter = new FormCounter { Name = name };
        counter.EnsureInitialized();
        template.Counters.Add(counter);
    }

    [RelayCommand]
    private void RemoveFormCounter(FormCounter? counter)
    {
        SelectedFormTemplate?.Counters.Remove(counter!);
    }

    [RelayCommand]
    private void AddFormActionStep()
    {
        SelectedFormTemplate?.ActionSteps.Add(new ActionStep());
    }

    [RelayCommand]
    private void RemoveFormActionStep(ActionStep? step)
    {
        var template = SelectedFormTemplate;
        if (template == null || step == null)
            return;

        template.ActionSteps.Remove(step);
    }

    [RelayCommand]
    private void MoveFormActionStepUp(ActionStep? step)
    {
        var template = SelectedFormTemplate;
        if (template == null || step == null)
            return;

        int index = template.ActionSteps.IndexOf(step);
        if (index > 0)
            template.ActionSteps.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveFormActionStepDown(ActionStep? step)
    {
        var template = SelectedFormTemplate;
        if (template == null || step == null)
            return;

        int index = template.ActionSteps.IndexOf(step);
        if (index >= 0 && index < template.ActionSteps.Count - 1)
            template.ActionSteps.Move(index, index + 1);
    }

    /// <summary>Overlay chip click: switches the form being filled in without
    /// touching the editor selection.</summary>
    [RelayCommand]
    private void SetFormTemplate(FormTemplate? template)
    {
        if (template == null)
            return;

        OverlayFormTemplate = template;
        IsFormHistoryVisible = false;
    }

    [RelayCommand]
    private void ToggleFormHistory()
    {
        IsFormHistoryVisible = !IsFormHistoryVisible;
    }

    #endregion

    #region Change tracking

    private void AttachFormTemplateHandlers(FormTemplate template)
    {
        template.PropertyChanged += OnFormModelPropertyChanged;
        template.Fields.CollectionChanged += OnFormFieldsCollectionChanged;
        template.Counters.CollectionChanged += OnFormPartsCollectionChanged;
        template.ActionSteps.CollectionChanged += OnFormActionStepsCollectionChanged;

        foreach (var field in template.Fields)
            AttachFormFieldHandlers(field);

        foreach (var counter in template.Counters)
            counter.PropertyChanged += OnFormModelPropertyChanged;

        foreach (var step in template.ActionSteps)
            step.PropertyChanged += OnFormModelPropertyChanged;

        _trackedFormTemplates.Add(template);
    }

    private void AttachFormFieldHandlers(FormField field)
    {
        field.PropertyChanged += OnFormModelPropertyChanged;
        field.Options.CollectionChanged += OnFormPartsCollectionChanged;

        foreach (var option in field.Options)
            option.PropertyChanged += OnFormModelPropertyChanged;
    }

    private void DetachFormTemplateHandlers()
    {
        foreach (var template in _trackedFormTemplates.ToList())
            DetachFormTemplateHandlers(template);
    }

    private void DetachFormTemplateHandlers(FormTemplate template)
    {
        template.PropertyChanged -= OnFormModelPropertyChanged;
        template.Fields.CollectionChanged -= OnFormFieldsCollectionChanged;
        template.Counters.CollectionChanged -= OnFormPartsCollectionChanged;
        template.ActionSteps.CollectionChanged -= OnFormActionStepsCollectionChanged;

        foreach (var field in template.Fields)
            DetachFormFieldHandlers(field);

        foreach (var counter in template.Counters)
            counter.PropertyChanged -= OnFormModelPropertyChanged;

        foreach (var step in template.ActionSteps)
            step.PropertyChanged -= OnFormModelPropertyChanged;

        _trackedFormTemplates.Remove(template);
    }

    private void DetachFormFieldHandlers(FormField field)
    {
        field.PropertyChanged -= OnFormModelPropertyChanged;
        field.Options.CollectionChanged -= OnFormPartsCollectionChanged;

        foreach (var option in field.Options)
            option.PropertyChanged -= OnFormModelPropertyChanged;
    }

    private void OnFormModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keys are shared token names; keep them normalized as the user types.
        if (sender is FormField changedField && e.PropertyName == nameof(FormField.Key))
        {
            string normalized = FormField.NormalizeKey(changedField.Key);
            if (!string.Equals(changedField.Key, normalized, StringComparison.Ordinal))
            {
                changedField.Key = normalized;
                return;
            }
        }

        // Follow the label with an auto-derived key as long as the user has not
        // set a key of their own (empty or still the generated "fieldN" one).
        if (sender is FormField labeledField && e.PropertyName == nameof(FormField.Label))
            TryDeriveFieldKeyFromLabel(labeledField);

        if (sender is FormCounter changedCounter && e.PropertyName == nameof(FormCounter.Name))
        {
            string normalized = FormField.NormalizeKey(changedCounter.Name);
            if (!string.Equals(changedCounter.Name, normalized, StringComparison.Ordinal))
            {
                changedCounter.Name = normalized;
                return;
            }
        }

        ScheduleAutoSave();
        RefreshFormEditorDerivedState();
    }

    private void TryDeriveFieldKeyFromLabel(FormField field)
    {
        bool keyIsAutoGenerated = string.IsNullOrWhiteSpace(field.Key)
            || System.Text.RegularExpressions.Regex.IsMatch(field.Key, "^field[0-9]+$");
        if (!keyIsAutoGenerated)
            return;

        string derived = FormField.NormalizeKey(field.Label);
        if (string.IsNullOrWhiteSpace(derived) || string.Equals(field.Key, derived, StringComparison.Ordinal))
            return;

        var template = SelectedFormTemplate;
        bool collides = template != null
            && (template.Fields.Any(other => !ReferenceEquals(other, field)
                    && string.Equals(other.Key, derived, StringComparison.OrdinalIgnoreCase))
                || template.Counters.Any(counter => string.Equals(counter.Name, derived, StringComparison.OrdinalIgnoreCase)));
        if (!collides)
            field.Key = derived;
    }

    private void OnFormFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (var field in e.OldItems.OfType<FormField>())
                DetachFormFieldHandlers(field);

        if (e.NewItems != null)
            foreach (var field in e.NewItems.OfType<FormField>())
                AttachFormFieldHandlers(field);

        ScheduleAutoSave();
        RefreshFormEditorDerivedState();
    }

    private void OnFormPartsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (var item in e.OldItems.OfType<ObservableObject>())
                item.PropertyChanged -= OnFormModelPropertyChanged;

        if (e.NewItems != null)
            foreach (var item in e.NewItems.OfType<ObservableObject>())
                item.PropertyChanged += OnFormModelPropertyChanged;

        ScheduleAutoSave();
        RefreshFormEditorDerivedState();
    }

    private void OnFormActionStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnFormPartsCollectionChanged(sender, e);
        OnPropertyChanged(nameof(HasFormSendAction));
    }

    #endregion

    #region Editor preview

    private void RefreshFormEditorDerivedState()
    {
        var template = SelectedFormTemplate;
        if (template == null)
        {
            FormEditorPreviewText = string.Empty;
            FormTemplateWarningText = string.Empty;
            FormTokenSuggestions = new ObservableCollection<string>();
            return;
        }

        var sampleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in template.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
                continue;

            string sample = !string.IsNullOrWhiteSpace(field.DefaultValue)
                ? FormRenderService.ExpandInlineTokens(template, field.DefaultValue)
                : field.Type == FormFieldType.Choice && field.Options.Count > 0
                    ? FormRenderService.ExpandInlineTokens(template, field.Options[0].Text)
                    : $"[{field.DisplayLabel}]";
            sampleValues.TryAdd(field.Key, sample);
            if (field.Type == FormFieldType.Choice)
            {
                sampleValues.TryAdd(
                    FormRenderService.GetChoiceToken(field.Key),
                    field.Options.FirstOrDefault()?.Label ?? string.Empty);
            }
        }

        var resolvedSamples = FormRenderService.ResolveFieldValues(template, sampleValues);
        FormEditorPreviewText = FormRenderService.Render(template, resolvedSamples);

        var warnings = FormRenderService.GetValidationErrors(template).ToList();
        var unknown = FormRenderService.GetUnknownTokens(template);
        if (unknown.Count > 0)
            warnings.Add($"Unknown tokens: {string.Join(", ", unknown.Select(token => $"{{{token}}}"))}");
        FormTemplateWarningText = string.Join(Environment.NewLine, warnings);

        var tokens = new List<string>();
        tokens.AddRange(template.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .Select(field => field.Key));
        tokens.AddRange(template.Fields
            .Where(field => field.Type == FormFieldType.Choice && !string.IsNullOrWhiteSpace(field.Key))
            .Select(field => FormRenderService.GetChoiceToken(field.Key)));
        tokens.AddRange(template.Counters
            .Where(counter => !string.IsNullOrWhiteSpace(counter.Name))
            .Select(counter => counter.Name));
        tokens.AddRange(FormRenderService.BuiltInTokens);
        FormTokenSuggestions = new ObservableCollection<string>(tokens.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    #endregion

    #region Overlay session

    private void RefreshFormSession()
    {
        var template = OverlayFormTemplate;
        if (template == null)
        {
            FormSessionFields = new ObservableCollection<FormFieldSessionViewModel>();
            FormPreviewText = string.Empty;
            FormSessionValidationText = string.Empty;
            return;
        }

        FormSessionValidationText = string.Join(Environment.NewLine, FormRenderService.GetValidationErrors(template));

        var sessionFields = template.Fields.Select(field => new FormFieldSessionViewModel(
            field,
            _formDataService.GetFieldHistory(_profile.Id, field.Id).ToList(),
            ExpandFormSessionPattern,
            OnFormSessionFieldChanged)).ToList();

        FormSessionFields = new ObservableCollection<FormFieldSessionViewModel>(sessionFields);

        // Second pass now that every session field exists, so cross-field tokens
        // in default values resolve against the right neighbors.
        foreach (var sessionField in FormSessionFields)
            sessionField.RefreshFromPattern();

        UpdateFormPreview();
    }

    /// <summary>Expands a session field's pattern against the live values of the
    /// other fields, then counters and built-ins. The field's own token is left
    /// literal so self-references cannot loop.</summary>
    private string ExpandFormSessionPattern(FormFieldSessionViewModel self, string pattern)
    {
        var template = OverlayFormTemplate;
        if (template == null)
            return pattern ?? string.Empty;

        return FormRenderService.ExpandWithResolver(template, pattern, token =>
        {
            if (string.Equals(token, self.Field.Key, StringComparison.OrdinalIgnoreCase))
                return null;

            if (self.IsChoice
                && string.Equals(token, FormRenderService.GetChoiceToken(self.Field.Key), StringComparison.OrdinalIgnoreCase))
            {
                return self.SelectedOption?.Label ?? string.Empty;
            }

            var other = FormSessionFields.FirstOrDefault(candidate => !ReferenceEquals(candidate, self)
                && string.Equals(candidate.Field.Key, token, StringComparison.OrdinalIgnoreCase));
            if (other != null)
                return other.Value;

            var otherChoice = FormSessionFields.FirstOrDefault(candidate => !ReferenceEquals(candidate, self)
                && candidate.IsChoice
                && string.Equals(
                    FormRenderService.GetChoiceToken(candidate.Field.Key),
                    token,
                    StringComparison.OrdinalIgnoreCase));
            return otherChoice?.SelectedOption?.Label ?? (otherChoice != null ? string.Empty : null);
        });
    }

    private bool _isPropagatingFormSessionChange;

    private void OnFormSessionFieldChanged()
    {
        if (_isPropagatingFormSessionChange)
            return;

        _isPropagatingFormSessionChange = true;
        try
        {
            foreach (var sessionField in FormSessionFields)
                sessionField.RefreshFromPattern();
        }
        finally
        {
            _isPropagatingFormSessionChange = false;
        }

        UpdateFormPreview();
    }

    private void UpdateFormPreview()
    {
        var template = OverlayFormTemplate;
        if (template == null)
        {
            FormPreviewText = string.Empty;
            return;
        }

        var resolvedValues = FormRenderService.ResolveFieldValues(template, BuildFormValueMap());
        FormPreviewText = FormRenderService.Render(template, resolvedValues);
    }

    private Dictionary<string, string> BuildFormValueMap()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in FormSessionFields)
        {
            if (!string.IsNullOrWhiteSpace(session.Field.Key))
            {
                values.TryAdd(session.Field.Key, session.Value ?? string.Empty);
                if (session.IsChoice)
                {
                    values.TryAdd(
                        FormRenderService.GetChoiceToken(session.Field.Key),
                        session.SelectedOption?.Label ?? string.Empty);
                }
            }
        }

        return values;
    }

    public string GetRenderedFormText()
    {
        UpdateFormPreview();
        return FormPreviewText;
    }

    /// <summary>The overlay's primary submit: records the submission, ticks the
    /// counters, and resets the form for the next entry.</summary>
    [RelayCommand]
    private async Task SaveFormResult()
    {
        string text = GetRenderedFormText();
        if (string.IsNullOrWhiteSpace(text))
            return;

        await CompleteFormSubmissionAsync(text);
    }

    [RelayCommand]
    private async Task CopyFormResult()
    {
        string text = GetRenderedFormText();
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!await CompleteFormSubmissionAsync(text))
            return;

        Helpers.ClipboardHelper.TrySetText(text);
    }

    public Task<bool> RecordFormSubmissionAsync(string renderedText)
    {
        var template = OverlayFormTemplate;
        if (template == null || string.IsNullOrWhiteSpace(renderedText))
            return Task.FromResult(false);

        return CompleteFormSubmissionAsync(renderedText);
    }

    /// <summary>Runs the template's action pipeline against the previously focused
    /// window. The overlay code-behind restores focus before calling this.</summary>
    public void ExecuteFormSendAction(string renderedText)
    {
        var template = OverlayFormTemplate;
        if (template == null || template.ActionSteps.Count == 0 || string.IsNullOrWhiteSpace(renderedText))
            return;

        _multiActionService.ExecuteWithItemText(template.ActionSteps.ToList(), renderedText, NaturalTypingEnabled);
    }

    private async Task<bool> CompleteFormSubmissionAsync(string renderedText)
    {
        var template = OverlayFormTemplate;
        if (template == null)
            return false;

        var validationErrors = FormRenderService.GetValidationErrors(template);
        FormSessionValidationText = string.Join(Environment.NewLine, validationErrors);
        if (validationErrors.Count > 0)
            return false;

        var submission = new FormSubmission
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            CreatedUtc = DateTime.UtcNow,
            RenderedText = renderedText
        };

        // Store the values with field-to-field tokens expanded, so history rows
        // and autocomplete suggestions show real text instead of raw {tokens}.
        var resolvedValues = FormRenderService.ResolveFieldValues(template, BuildFormValueMap());
        submission.OutputTemplateSnapshot = FormRenderService.CreateSubmissionTemplateSnapshot(template);
        var historyEntries = new List<KeyValuePair<string, string>>();
        foreach (var session in FormSessionFields)
        {
            string value = !string.IsNullOrWhiteSpace(session.Field.Key)
                && resolvedValues.TryGetValue(session.Field.Key, out string? resolved)
                    ? resolved
                    : session.Value ?? string.Empty;
            submission.Values[session.Label] = value;
            submission.FieldIds[session.Label] = session.Field.Id;
            if (!string.IsNullOrWhiteSpace(session.Field.Key))
            {
                submission.TokenValues[session.Field.Key] = value;
                submission.FieldTokens[session.Label] = session.Field.Key;
                if (session.IsChoice)
                {
                    string choiceToken = FormRenderService.GetChoiceToken(session.Field.Key);
                    submission.TokenValues[choiceToken] = resolvedValues.TryGetValue(choiceToken, out string? choiceLabel)
                        ? choiceLabel
                        : session.SelectedOption?.Label ?? string.Empty;
                }
            }
            if (session.Field.RememberHistory && !string.IsNullOrWhiteSpace(value))
                historyEntries.Add(new KeyValuePair<string, string>(session.Field.Id, value));
        }

        submission.RenderedText = FormRenderService.RenderTemplate(
            submission.OutputTemplateSnapshot,
            submission.TokenValues);

        var previousCounterValues = template.Counters
            .Select(counter => (Counter: counter, counter.NextValue))
            .ToList();
        if (template.Counters.Count > 0)
        {
            foreach (var counter in template.Counters)
                counter.NextValue++;

            // Persist the reservation first. A crash after this point may leave a
            // harmless gap, but can never cause an already-issued number to repeat.
            if (!await SavePendingChangesImmediatelyAsync())
            {
                foreach (var (counter, previousValue) in previousCounterValues)
                    counter.NextValue = previousValue;
                return false;
            }
        }

        if (!_formDataService.RecordSubmission(_profile.Id, submission, historyEntries))
            return false;

        // Fresh session: new counter values in defaults, updated history chips.
        RefreshFormSession();
        LoadFormSubmissions();
        return true;
    }

    #endregion

    #region Submission history

    private void LoadFormSubmissions()
    {
        var submissions = _formDataService.GetSubmissions(_profile.Id);
        FormSubmissions = new ObservableCollection<FormSubmissionViewModel>(
            submissions.Select(submission => new FormSubmissionViewModel(submission)));
        OverlayFormSubmissions = new ObservableCollection<OverlayFormSubmissionViewModel>(
            submissions.Select(submission => new OverlayFormSubmissionViewModel(
                submission,
                (vm, completed) =>
                {
                    bool saved = _formDataService.SetSubmissionCompleted(_profile.Id, vm.Id, completed);
                    if (saved)
                        NotifyOverlayFormSubmissionCountsChanged();
                    return saved;
                },
                (vm, label, value) =>
                {
                    string? fieldId = GetOverlaySubmissionFieldId(vm.Model, label);
                    bool saved = _formDataService.UpdateSubmissionField(
                        _profile.Id,
                        vm.Id,
                        label,
                        value,
                        fieldId);
                    if (saved)
                    {
                        RefreshFormSession();
                        LoadFormSubmissions();
                    }
                    return saved;
                },
                label => CanEditOverlaySubmissionField(submission, label))));
        OnPropertyChanged(nameof(HasFormSubmissions));
        OnPropertyChanged(nameof(HasOverlayFormSubmissions));
    }

    private void NotifyOverlayFormSubmissionCountsChanged()
    {
        OnPropertyChanged(nameof(OpenOverlayFormSubmissionCount));
        OnPropertyChanged(nameof(CompletedOverlayFormSubmissionCount));
    }

    private IEnumerable<OverlayFormSubmissionViewModel> GetOverlayFormSubmissionsForCount()
    {
        if (!FormsHistoryCountsTodayOnly)
            return OverlayFormSubmissions;

        DateTime today = DateTime.Today;
        return OverlayFormSubmissions.Where(submission => submission.Model.CreatedUtc.ToLocalTime().Date == today);
    }

    private bool CanEditOverlaySubmissionField(FormSubmission submission, string label)
    {
        if (string.IsNullOrWhiteSpace(submission.OutputTemplateSnapshot)
            || !submission.FieldTokens.ContainsKey(label))
        {
            return false;
        }

        var template = FormTemplates.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, submission.TemplateId, StringComparison.Ordinal));
        return template?.Fields.Any(field =>
            field.AllowHistoryEditing
            && string.Equals(field.DisplayLabel, label, StringComparison.Ordinal)) == true;
    }

    private string? GetOverlaySubmissionFieldId(FormSubmission submission, string label)
    {
        if (submission.FieldIds.TryGetValue(label, out string? storedId)
            && !string.IsNullOrWhiteSpace(storedId))
        {
            return storedId;
        }

        var template = FormTemplates.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, submission.TemplateId, StringComparison.Ordinal));
        return template?.Fields.FirstOrDefault(field =>
            string.Equals(field.DisplayLabel, label, StringComparison.Ordinal))?.Id;
    }

    [RelayCommand]
    private void CopyFormSubmission(FormSubmissionViewModel? submission)
    {
        if (submission == null || string.IsNullOrWhiteSpace(submission.RenderedText))
            return;

        try
        {
            System.Windows.Clipboard.SetText(submission.RenderedText);
        }
        catch
        {
            // Clipboard can fail if another process has it locked.
        }
    }

    [RelayCommand]
    private void DeleteFormSubmission(FormSubmissionViewModel? submission)
    {
        if (submission == null)
            return;

        if (_formDataService.DeleteSubmission(_profile.Id, submission.Id))
            LoadFormSubmissions();
    }

    [RelayCommand]
    private void ClearFormSubmissions()
    {
        if (FormSubmissions.Count == 0)
            return;

        bool confirmed = Helpers.ConfirmDialog.Show(
            System.Windows.Application.Current?.MainWindow,
            "Clear form history",
            $"Delete all {FormSubmissions.Count} stored submission(s) for this profile?",
            confirmText: "Delete all",
            danger: true);
        if (!confirmed)
            return;

        _formDataService.ClearSubmissions(_profile.Id);
        LoadFormSubmissions();
    }

    #endregion
}
