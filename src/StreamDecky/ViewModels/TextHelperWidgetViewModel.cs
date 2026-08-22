using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Helpers;
using StreamDecky.Services;

namespace StreamDecky.ViewModels;

/// <summary>
/// Backs the overlay's dyslexia-friendly scratch pad: type a line, let DeepSeek
/// respell it, then copy it. Copying never depends on the spell check having run.
/// </summary>
public partial class TextHelperWidgetViewModel : ObservableObject, IDisposable
{
    private readonly AppSettingsService _appSettings;
    private readonly DeepSeekSpellCheckService _spellCheckService;

    private CancellationTokenSource? _spellCheckCancellation;
    private string? _textBeforeSpellCheck;
    private string? _textAfterSpellCheck;
    private bool _isDisposed;

    public TextHelperWidgetViewModel(
        AppSettingsService appSettings,
        DeepSeekSpellCheckService? spellCheckService = null)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _spellCheckService = spellCheckService ?? new DeepSeekSpellCheckService();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SpellCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(HasText))]
    [NotifyPropertyChangedFor(nameof(CharacterCountText))]
    private string _text = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SpellCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoSpellCheckCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Drives the status line colour; errors read red, confirmations read green.</summary>
    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoSpellCheckCommand))]
    private bool _canUndoSpellCheck;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public bool IsApiKeyMissing => !_appSettings.HasDeepSeekApiKey;

    public string CharacterCountText => $"{Text.Length}/{DeepSeekSpellCheckService.MaxInputLength}";

    partial void OnTextChanged(string value)
    {
        // Any edit invalidates both the previous status line and the stored undo text.
        if (!string.IsNullOrEmpty(StatusText))
        {
            StatusText = string.Empty;
            IsStatusError = false;
        }

        if (CanUndoSpellCheck && !string.Equals(value, _textAfterSpellCheck, StringComparison.Ordinal))
        {
            CanUndoSpellCheck = false;
            _textBeforeSpellCheck = null;
        }
    }

    /// <summary>Called when the overlay opens so a missing key is reported straight away.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsApiKeyMissing));
    }

    [RelayCommand(CanExecute = nameof(CanSpellCheck))]
    private async Task SpellCheckAsync()
    {
        _spellCheckCancellation?.Cancel();
        _spellCheckCancellation?.Dispose();
        _spellCheckCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _spellCheckCancellation.Token;

        string original = Text;
        IsBusy = true;
        IsStatusError = false;
        StatusText = "Checking spelling…";

        try
        {
            SpellCheckResult result = await _spellCheckService.CorrectAsync(
                original,
                _appSettings.DeepSeekApiKey,
                _appSettings.DeepSeekModel,
                _appSettings.SpellCheckPrompt,
                _appSettings.SpellCheckThinking,
                cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
                return;

            if (!result.Success)
            {
                SetStatus(result.ErrorMessage ?? "Spell check failed.", isError: true);
                OnPropertyChanged(nameof(IsApiKeyMissing));
                return;
            }

            // The box stays editable during the call, so a correction of text the user
            // has already moved on from must be dropped rather than overwrite them.
            if (!string.Equals(Text, original, StringComparison.Ordinal))
            {
                SetStatus("You changed the text while it was being checked, so the correction was dropped.", isError: false);
                return;
            }

            if (string.Equals(result.Text, original, StringComparison.Ordinal))
            {
                SetStatus("Already spelled correctly.", isError: false);
                return;
            }

            _textBeforeSpellCheck = original;
            _textAfterSpellCheck = result.Text;
            Text = result.Text;
            CanUndoSpellCheck = true;
            SetStatus("Spelling fixed. Copy it, or undo to get your own words back.", isError: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSpellCheck() => !IsBusy && HasText;

    [RelayCommand(CanExecute = nameof(HasText))]
    private void Copy()
    {
        if (ClipboardHelper.TrySetText(Text))
            SetStatus("Copied to the clipboard.", isError: false);
        else
            SetStatus("Could not copy — another program is holding the clipboard. Try again.", isError: true);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoSpellCheck()
    {
        if (_textBeforeSpellCheck == null)
            return;

        string restored = _textBeforeSpellCheck;
        _textBeforeSpellCheck = null;
        _textAfterSpellCheck = null;
        CanUndoSpellCheck = false;
        Text = restored;
        SetStatus("Your original text is back.", isError: false);
    }

    private bool CanUndo() => CanUndoSpellCheck && !IsBusy;

    [RelayCommand(CanExecute = nameof(HasText))]
    private void Clear()
    {
        // Clearing is an explicit "I am done with this line", so stop waiting on any
        // correction still in flight for it.
        _spellCheckCancellation?.Cancel();

        _textBeforeSpellCheck = null;
        _textAfterSpellCheck = null;
        CanUndoSpellCheck = false;
        Text = string.Empty;
        SetStatus(string.Empty, isError: false);
    }

    private void SetStatus(string message, bool isError)
    {
        IsStatusError = isError;
        StatusText = message;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _spellCheckCancellation?.Cancel();
        _spellCheckCancellation?.Dispose();
        _spellCheckCancellation = null;
        GC.SuppressFinalize(this);
    }
}
