using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;
using StreamDecky.Services;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Suggestions for the editable font box: wide, open shapes with distinct
    /// letterforms, all shipped with Windows so they always resolve.
    /// </summary>
    public IReadOnlyList<string> TextHelperFontOptions { get; } = new[]
    {
        "Verdana",
        "Tahoma",
        "Segoe UI",
        "Arial",
        "Comic Sans MS",
        "Trebuchet MS",
        "Consolas"
    };

    private TextHelperWidgetViewModel? _textHelperWidget;

    /// <summary>Created lazily so sessions that never open the widget pay no HttpClient cost.</summary>
    public TextHelperWidgetViewModel TextHelperWidget =>
        _textHelperWidget ??= new TextHelperWidgetViewModel(_appSettingsService, SpellCheckService);

    /// <summary>
    /// The DeepSeek key lives in app-settings.json rather than the profile, so it is
    /// never carried along by a profile export.
    /// </summary>
    public string DeepSeekApiKey
    {
        get => _appSettingsService.DeepSeekApiKey;
        set
        {
            if (string.Equals(_appSettingsService.DeepSeekApiKey, value?.Trim() ?? string.Empty, StringComparison.Ordinal))
                return;

            bool stored = _appSettingsService.TrySetDeepSeekApiKey(value, out string? error);

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDeepSeekApiKey));

            // A protection failure is the one case where the key silently would not be
            // there next time, so it is reported in the same place as the key test.
            IsDeepSeekTestError = !stored;
            DeepSeekTestStatus = stored ? string.Empty : error ?? string.Empty;

            TestDeepSeekConnectionCommand.NotifyCanExecuteChanged();
            _textHelperWidget?.Refresh();
        }
    }

    public bool HasDeepSeekApiKey => _appSettingsService.HasDeepSeekApiKey;

    public string DeepSeekModel
    {
        get => _appSettingsService.DeepSeekModel;
        set
        {
            if (string.Equals(_appSettingsService.DeepSeekModel, value, StringComparison.Ordinal))
                return;

            _appSettingsService.DeepSeekModel = value;
            OnPropertyChanged();
        }
    }

    public string SpellCheckPrompt
    {
        get => _appSettingsService.SpellCheckPrompt;
        set
        {
            if (string.Equals(_appSettingsService.SpellCheckPrompt, value, StringComparison.Ordinal))
                return;

            _appSettingsService.SpellCheckPrompt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSpellCheckPromptCustomised));
        }
    }

    /// <summary>Labels for the thinking picker, mapped to the API values in <see cref="AppSettings.ThinkingLevels"/>.</summary>
    public IReadOnlyList<string> SpellCheckThinkingOptions { get; } = new[]
    {
        "Off — fastest",
        "Low",
        "High",
        "Max"
    };

    public string SelectedSpellCheckThinkingOption
    {
        get
        {
            int index = AppSettings.ThinkingLevels.ToList().IndexOf(_appSettingsService.SpellCheckThinking);
            return SpellCheckThinkingOptions[index < 0 ? 0 : index];
        }
        set
        {
            int index = SpellCheckThinkingOptions.ToList().IndexOf(value);
            string level = index < 0 ? AppSettings.ThinkingDisabled : AppSettings.ThinkingLevels[index];
            if (string.Equals(_appSettingsService.SpellCheckThinking, level, StringComparison.Ordinal))
                return;

            _appSettingsService.SpellCheckThinking = level;
            OnPropertyChanged();
        }
    }

    public bool IsSpellCheckPromptCustomised =>
        !string.Equals(SpellCheckPrompt, AppSettings.DefaultSpellCheckPrompt, StringComparison.Ordinal);

    [RelayCommand]
    private void ResetSpellCheckPrompt()
    {
        SpellCheckPrompt = AppSettings.DefaultSpellCheckPrompt;
        OnPropertyChanged(nameof(SpellCheckPrompt));
    }

    [RelayCommand(CanExecute = nameof(CanTestDeepSeekConnection))]
    private async Task TestDeepSeekConnectionAsync()
    {
        IsDeepSeekTestRunning = true;
        IsDeepSeekTestError = false;
        DeepSeekTestStatus = "Contacting DeepSeek…";

        try
        {
            SpellCheckResult result = await SpellCheckService.CorrectAsync(
                "this is a smal test setnence",
                _appSettingsService.DeepSeekApiKey,
                _appSettingsService.DeepSeekModel,
                _appSettingsService.SpellCheckPrompt,
                _appSettingsService.SpellCheckThinking).ConfigureAwait(true);

            IsDeepSeekTestError = !result.Success;
            DeepSeekTestStatus = result.Success
                ? $"Works. DeepSeek answered: “{result.Text}”"
                : result.ErrorMessage ?? "The test failed.";
        }
        finally
        {
            IsDeepSeekTestRunning = false;
        }
    }

    private bool CanTestDeepSeekConnection() => !IsDeepSeekTestRunning && HasDeepSeekApiKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestDeepSeekConnectionCommand))]
    private bool _isDeepSeekTestRunning;

    [ObservableProperty]
    private string _deepSeekTestStatus = string.Empty;

    [ObservableProperty]
    private bool _isDeepSeekTestError;

    public bool TextHelperVisible
    {
        get => _profile.TextHelperVisible;
        set
        {
            if (_profile.TextHelperVisible == value)
                return;

            _profile.TextHelperVisible = value;
            OnPropertyChanged();
            ScheduleAutoSave();

            if (value)
                TextHelperWidget.Refresh();
        }
    }

    public double TextHelperX
    {
        get => _profile.TextHelperX;
        set
        {
            double clamped = Math.Max(0, value);
            if (Math.Abs(_profile.TextHelperX - clamped) < 0.001)
                return;

            _profile.TextHelperX = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double TextHelperY
    {
        get => _profile.TextHelperY;
        set
        {
            double clamped = Math.Max(0, value);
            if (Math.Abs(_profile.TextHelperY - clamped) < 0.001)
                return;

            _profile.TextHelperY = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double TextHelperWidth
    {
        get => _profile.TextHelperWidth;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinTextHelperWidth, DeckProfile.MaxTextHelperWidth);
            if (Math.Abs(_profile.TextHelperWidth - clamped) < 0.001)
                return;

            _profile.TextHelperWidth = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double TextHelperHeight
    {
        get => _profile.TextHelperHeight;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinTextHelperHeight, DeckProfile.MaxTextHelperHeight);
            if (Math.Abs(_profile.TextHelperHeight - clamped) < 0.001)
                return;

            _profile.TextHelperHeight = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    /// <summary>
    /// Off-white reads easier for most people with dyslexia, but it stands out hard
    /// against a dark overlay, so the dark variant is one click away.
    /// </summary>
    public bool TextHelperDarkTextArea
    {
        get => _profile.TextHelperDarkTextArea;
        set
        {
            if (_profile.TextHelperDarkTextArea == value)
                return;

            _profile.TextHelperDarkTextArea = value;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public string TextHelperFontFamily
    {
        get => _profile.TextHelperFontFamily;
        set
        {
            string family = string.IsNullOrWhiteSpace(value) ? DeckProfile.DefaultTextHelperFontFamily : value;
            if (string.Equals(_profile.TextHelperFontFamily, family, StringComparison.Ordinal))
                return;

            _profile.TextHelperFontFamily = family;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    public double TextHelperFontSize
    {
        get => _profile.TextHelperFontSize;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinTextHelperFontSize, DeckProfile.MaxTextHelperFontSize);
            if (Math.Abs(_profile.TextHelperFontSize - clamped) < 0.001)
                return;

            _profile.TextHelperFontSize = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    [RelayCommand]
    private void ToggleTextHelper()
    {
        TextHelperVisible = !TextHelperVisible;
    }

    [RelayCommand]
    private void HideTextHelper()
    {
        TextHelperVisible = false;
    }

    private void RefreshTextHelperIfVisible()
    {
        if (TextHelperVisible)
            TextHelperWidget.Refresh();
    }
}
