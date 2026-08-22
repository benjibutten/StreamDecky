using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Helpers;
using StreamDecky.Models;
using StreamDecky.Services;

namespace StreamDecky.ViewModels;

/// <summary>
/// Backs the overlay's dyslexia-friendly scratch pad: type a line, let DeepSeek respell
/// it, then copy it. Copying never depends on the spell check having run.
/// <para>
/// The same box also asks quick questions, but an answer is never written back into
/// <see cref="Text"/>. Spell checking replaces what you wrote; asking produces something
/// new alongside it. Keeping the answer in its own place is what stops one box from
/// meaning two different things, and it leaves undo and copy attached to the text they
/// have always been attached to.
/// </para>
/// </summary>
public partial class TextHelperWidgetViewModel : ObservableObject, IDisposable
{
    private readonly AppSettingsService _appSettings;
    private readonly DeepSeekSpellCheckService _spellCheckService;
    private readonly QuickAnswerService _quickAnswerService;

    private CancellationTokenSource? _spellCheckCancellation;
    private CancellationTokenSource? _askCancellation;
    private bool _hasEverShownAnswer;
    private string? _textBeforeSpellCheck;
    private string? _textAfterSpellCheck;
    private bool _isDisposed;

    public TextHelperWidgetViewModel(
        AppSettingsService appSettings,
        DeepSeekSpellCheckService? spellCheckService = null,
        QuickAnswerService? quickAnswerService = null)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _spellCheckService = spellCheckService ?? new DeepSeekSpellCheckService();
        _quickAnswerService = quickAnswerService ?? new QuickAnswerService();
    }

    /// <summary>
    /// Raised when an answer first appears, so the overlay can make room for it. A widget
    /// sized for one line of text has nowhere to put an answer and three sources.
    /// </summary>
    public event EventHandler? AnswerShown;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SpellCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(HasText))]
    [NotifyPropertyChangedFor(nameof(CharacterCountText))]
    [NotifyPropertyChangedFor(nameof(IsTooLongToAsk))]
    private string _text = string.Empty;

    /// <summary>True while a spell check is in flight. Drives the spell button's own label.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SpellCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoSpellCheckCommand))]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    private bool _isBusy;

    /// <summary>True while a question is in flight. Drives the ask button's own label.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SpellCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoSpellCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissAnswerCommand))]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(IsAnswerAreaVisible))]
    private bool _isAsking;

    /// <summary>Either call is in flight. Both actions bill the same key, so they take turns.</summary>
    public bool IsWorking => IsBusy || IsAsking;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Drives the status line colour; errors read red, confirmations read green.</summary>
    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoSpellCheckCommand))]
    private bool _canUndoSpellCheck;

    /// <summary>
    /// The question that produced <see cref="AnswerText"/>. Shown above the answer so an
    /// answer left on screen while the box is retyped can never look like a reply to what
    /// is in the box now.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswer))]
    private string _askedQuestion = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(HasAnswer))]
    [NotifyPropertyChangedFor(nameof(IsAnswerAreaVisible))]
    private string _answerText = string.Empty;

    /// <summary>
    /// Whether the answer was checked against a web search. Never inferred from the source
    /// count at the view layer: an answer with no sources has to say out loud that it is
    /// the model's own knowledge, which is the whole basis for trusting the feature.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnswerOriginText))]
    private bool _answerUsedSearch;

    public ObservableCollection<QuickAnswerSource> AnswerSources { get; } = new();

    public bool HasAnswer => !string.IsNullOrWhiteSpace(AnswerText);

    /// <summary>
    /// The answer card is on screen from the moment the question is sent, not from the
    /// moment it is answered, so the progress line appears where the answer will land
    /// rather than making the eye travel twice.
    /// <para>
    /// It also has to survive a failure. A question that ends in an error leaves no
    /// answer, and without the status line keeping the card open the error would be
    /// written into a panel that collapses in the same breath — which looks exactly like
    /// the button doing nothing at all.
    /// </para>
    /// </summary>
    public bool IsAnswerAreaVisible => HasAnswer || IsAsking || !string.IsNullOrEmpty(AskStatusText);

    /// <summary>The label on the answer card that says where the answer came from.</summary>
    public string AnswerOriginText => AnswerUsedSearch
        ? "Checked against the web"
        : "From the model, not checked against the web";

    /// <summary>Progress for the question, kept apart from the spell check's status line.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissAnswerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(IsAnswerAreaVisible))]
    private string _askStatusText = string.Empty;

    [ObservableProperty]
    private bool _isAskStatusError;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public bool IsApiKeyMissing => !_appSettings.HasDeepSeekApiKey;

    /// <summary>
    /// A question is one line, not the 4000 characters the writing box allows. Catching it
    /// here keeps a stray paste from being sent and billed as a question.
    /// </summary>
    public bool IsTooLongToAsk => Text.Trim().Length > QuickAnswerService.MaxQuestionLength;

    /// <summary>
    /// One notice line covering both actions, so a missing key is never reported as if
    /// the whole widget were broken.
    /// </summary>
    public string SetupNoticeText
    {
        get
        {
            if (!_appSettings.HasDeepSeekApiKey)
                return "Add your DeepSeek API key in Settings to fix spelling and ask questions.";

            if (!_appSettings.HasBraveApiKey
                && !string.Equals(_appSettings.QuickAnswerSearchMode, AppSettings.SearchModeNever, StringComparison.Ordinal))
            {
                return "No Brave Search key: answers come from the model alone and are not checked against the web.";
            }

            return string.Empty;
        }
    }

    public bool HasSetupNotice => !string.IsNullOrEmpty(SetupNoticeText);

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

        // The answer deliberately survives an edit: reading it while typing the next
        // question is the normal way to use this. AskedQuestion is what keeps it honest.
        // The status line does not: a stale error, or a "copied" confirmation, has nothing
        // to do with what is being typed now, and clearing it also takes away an error
        // card that has no answer under it.
        if (!string.IsNullOrEmpty(AskStatusText))
        {
            AskStatusText = string.Empty;
            IsAskStatusError = false;
        }
    }

    /// <summary>Called when the overlay opens so missing keys are reported straight away.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsApiKeyMissing));
        OnPropertyChanged(nameof(SetupNoticeText));
        OnPropertyChanged(nameof(HasSetupNotice));
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
                Refresh();
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

    private bool CanSpellCheck() => !IsWorking && HasText;

    /// <summary>
    /// Answers the question in the box without touching the box. The answer replaces any
    /// previous one, because two answers on a widget this size is one too many.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync()
    {
        _askCancellation?.Cancel();
        _askCancellation?.Dispose();
        _askCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _askCancellation.Token;

        string question = Text.Trim();

        // The previous answer goes before the new question is sent, not when the new one
        // arrives. Leaving it up means a failure would show the old answer, the old
        // question and a new error side by side, which reads as though the answer already
        // on screen had somehow failed.
        ClearAnswer();

        IsAsking = true;
        IsAskStatusError = false;

        var progress = new Progress<QuickAnswerStage>(stage => AskStatusText = DescribeStage(stage));
        AskStatusText = DescribeStage(QuickAnswerStage.Deciding);

        try
        {
            QuickAnswerResult result = await _quickAnswerService.AskAsync(
                new QuickAnswerRequest(
                    question,
                    _appSettings.DeepSeekApiKey,
                    _appSettings.BraveApiKey,
                    _appSettings.DeepSeekModel,
                    _appSettings.QuickAnswerPrompt,
                    _appSettings.QuickAnswerThinking,
                    _appSettings.QuickAnswerSearchMode),
                progress,
                cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
                return;

            if (!result.Success)
            {
                IsAskStatusError = true;
                AskStatusText = result.ErrorMessage ?? "The question failed.";
                Refresh();
                return;
            }

            ShowAnswer(question, result);
        }
        finally
        {
            IsAsking = false;
        }
    }

    private bool CanAsk() => !IsWorking && HasText && !IsTooLongToAsk;

    private void ShowAnswer(string question, QuickAnswerResult result)
    {
        bool isFirstAnswer = !_hasEverShownAnswer;
        _hasEverShownAnswer = true;

        AnswerSources.Clear();
        foreach (QuickAnswerSource source in result.Sources)
            AnswerSources.Add(source);

        AskedQuestion = question;
        AnswerUsedSearch = result.UsedSearch;
        AnswerText = result.Answer;
        AskStatusText = string.Empty;
        IsAskStatusError = false;

        // Only the first answer needs room made for it; after that the user has whatever
        // size they settled on and it is not ours to keep changing.
        if (isFirstAnswer)
            AnswerShown?.Invoke(this, EventArgs.Empty);
    }

    private static string DescribeStage(QuickAnswerStage stage) => stage switch
    {
        QuickAnswerStage.Searching => "Searching the web…",
        QuickAnswerStage.Writing => "Writing the answer…",
        _ => "Thinking…"
    };

    [RelayCommand(CanExecute = nameof(HasText))]
    private void Copy()
    {
        if (ClipboardHelper.TrySetText(Text))
            SetStatus("Copied to the clipboard.", isError: false);
        else
            SetStatus("Could not copy — another program is holding the clipboard. Try again.", isError: true);
    }

    [RelayCommand(CanExecute = nameof(HasAnswer))]
    private void CopyAnswer()
    {
        if (ClipboardHelper.TrySetText(AnswerText))
        {
            IsAskStatusError = false;
            AskStatusText = "Answer copied to the clipboard.";
        }
        else
        {
            IsAskStatusError = true;
            AskStatusText = "Could not copy — another program is holding the clipboard. Try again.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanDismissAnswer))]
    private void DismissAnswer()
    {
        // An answer still being written is not worth keeping once it has been dismissed.
        _askCancellation?.Cancel();
        ClearAnswer();
    }

    private void ClearAnswer()
    {
        AnswerSources.Clear();
        AskedQuestion = string.Empty;
        AnswerText = string.Empty;
        AnswerUsedSearch = false;
        AskStatusText = string.Empty;
        IsAskStatusError = false;
    }

    private bool CanDismissAnswer() => IsAnswerAreaVisible && !IsAsking;

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

    private bool CanUndo() => CanUndoSpellCheck && !IsWorking;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        // Clearing is an explicit "I am done with this line", so stop waiting on anything
        // still in flight for it.
        _spellCheckCancellation?.Cancel();

        _textBeforeSpellCheck = null;
        _textAfterSpellCheck = null;
        CanUndoSpellCheck = false;
        Text = string.Empty;
        SetStatus(string.Empty, isError: false);

        DismissAnswer();
    }

    private bool CanClear() => HasText || IsAnswerAreaVisible;

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
        _askCancellation?.Cancel();
        _askCancellation?.Dispose();
        _askCancellation = null;
        GC.SuppressFinalize(this);
    }
}
