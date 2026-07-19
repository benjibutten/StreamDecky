using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private int _currentNotePageIndex;

    [ObservableProperty]
    private ObservableCollection<StickyNoteViewModel> _stickyNotes = new();

    [ObservableProperty]
    private bool _stickyNotesVisible;

    partial void OnCurrentNotePageIndexChanged(int value)
    {
        if (_profile.NotePages.Count == 0)
            return;

        int clamped = Math.Clamp(value, 0, _profile.NotePages.Count - 1);
        if (clamped != value)
        {
            CurrentNotePageIndex = clamped;
            return;
        }

        _profile.CurrentNotePageIndex = clamped;
        LoadStickyNotes();
        NotifyNotePageChanged();
        ScheduleAutoSave();
    }

    public string CurrentNotePageName => CurrentNotePage.Name;
    public int NotePageCount => _profile.NotePages.Count;
    public bool CanGoToPreviousNotePage => CurrentNotePageIndex > 0;
    public bool CanGoToNextNotePage => CurrentNotePageIndex < _profile.NotePages.Count - 1;
    public bool CanRemoveNotePage => NotePageCount > 1;
    public string NotePageIndicator => $"{CurrentNotePageIndex + 1} / {NotePageCount}";
    public bool HasMultipleNotePages => NotePageCount > 1;
    public int CurrentNotePageNoteCount => CurrentNotePage.StickyNotes.Count;
    public bool HasStickyNotes => StickyNotes.Count > 0;

    public double StickyNoteFontSize
    {
        get => _profile.StickyNoteFontSize;
        set
        {
            double clamped = Math.Clamp(value, DeckProfile.MinStickyNoteFontSize, DeckProfile.MaxStickyNoteFontSize);
            if (Math.Abs(_profile.StickyNoteFontSize - clamped) < 0.001)
                return;

            _profile.StickyNoteFontSize = clamped;
            OnPropertyChanged();
            ScheduleAutoSave();
        }
    }

    private NotePage CurrentNotePage => _profile.NotePages[Math.Clamp(CurrentNotePageIndex, 0, _profile.NotePages.Count - 1)];

    private void LoadStickyNotes()
    {
        var noteViewModels = new List<StickyNoteViewModel>(CurrentNotePage.StickyNotes.Count);
        foreach (var note in CurrentNotePage.StickyNotes)
        {
            noteViewModels.Add(new StickyNoteViewModel(note, ScheduleAutoSave));
        }

        StickyNotes = new ObservableCollection<StickyNoteViewModel>(noteViewModels);

        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
    }

    [RelayCommand]
    private void PreviousNotePage()
    {
        if (CanGoToPreviousNotePage)
            CurrentNotePageIndex--;
    }

    [RelayCommand]
    private void NextNotePage()
    {
        if (CanGoToNextNotePage)
            CurrentNotePageIndex++;
    }

    [RelayCommand]
    private void AddNotePage()
    {
        var notePage = new NotePage
        {
            Name = $"Notes {_profile.NotePages.Count + 1}"
        };
        notePage.EnsureInitialized();

        _profile.NotePages.Add(notePage);
        CurrentNotePageIndex = _profile.NotePages.Count - 1;
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveNotePage()
    {
        if (_profile.NotePages.Count <= 1)
            return;

        int removedIndex = CurrentNotePageIndex;
        _profile.NotePages.RemoveAt(removedIndex);

        if (CurrentNotePageIndex >= _profile.NotePages.Count)
        {
            CurrentNotePageIndex = _profile.NotePages.Count - 1;
        }
        else
        {
            _profile.CurrentNotePageIndex = CurrentNotePageIndex;
            LoadStickyNotes();
            NotifyNotePageChanged();
        }

        ScheduleAutoSave();
    }

    private void NotifyNotePageChanged()
    {
        OnPropertyChanged(nameof(CurrentNotePageName));
        OnPropertyChanged(nameof(NotePageCount));
        OnPropertyChanged(nameof(CanGoToPreviousNotePage));
        OnPropertyChanged(nameof(CanGoToNextNotePage));
        OnPropertyChanged(nameof(CanRemoveNotePage));
        OnPropertyChanged(nameof(NotePageIndicator));
        OnPropertyChanged(nameof(HasMultipleNotePages));
        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
    }

    [RelayCommand]
    private void ToggleStickyNotes()
    {
        StickyNotesVisible = true;
    }

    [RelayCommand]
    private void AddStickyNote()
    {
        int offset = StickyNotes.Count * 20;
        var note = new StickyNote
        {
            Title = $"Sticky note {StickyNotes.Count + 1}",
            Text = string.Empty,
            X = 96 + offset,
            Y = 140 + offset,
            Width = 230,
            Height = 180,
            Color = "#F8E784"
        };

        CurrentNotePage.StickyNotes.Add(note);
        StickyNotes.Add(new StickyNoteViewModel(note, ScheduleAutoSave));

        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
        ScheduleAutoSave();
    }

    [RelayCommand]
    private void RemoveStickyNote(StickyNoteViewModel? note)
    {
        if (note == null)
            return;

        CurrentNotePage.StickyNotes.Remove(note.Model);
        StickyNotes.Remove(note);

        OnPropertyChanged(nameof(HasStickyNotes));
        OnPropertyChanged(nameof(CurrentNotePageNoteCount));
        ScheduleAutoSave();
    }

    public void SetStickyNoteColor(StickyNoteViewModel? note, string color)
    {
        if (note == null || string.IsNullOrWhiteSpace(color))
            return;

        note.Color = color;
    }
}