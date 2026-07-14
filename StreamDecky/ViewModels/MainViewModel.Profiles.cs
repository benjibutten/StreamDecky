using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Models;
using StreamDecky.Services;

namespace StreamDecky.ViewModels;

public partial class MainViewModel
{
    private static readonly JsonSerializerOptions ProfileCloneJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ObservableCollection<ProfileOption> ProfileOptions { get; } = new();

    [ObservableProperty]
    private string? _selectedProfileId;

    partial void OnSelectedProfileIdChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            SwitchToProfileById(value);
    }

    public DeckProfile Profile => _profile;
    public string ActiveProfileName => _profile.Name;
    public int ProfileCount => _profileStore.Profiles.Count;
    public bool CanRemoveProfile => ProfileCount > 1;
    public string ProfileIndicator => $"{GetActiveProfileIndex() + 1} / {ProfileCount}";

    private int GetActiveProfileIndex()
    {
        int index = _profileStore.Profiles.FindIndex(profile => string.Equals(profile.Id, _profile.Id, StringComparison.Ordinal));
        return index >= 0 ? index : 0;
    }

    private string CreateUniqueProfileName(string baseName)
    {
        baseName = string.IsNullOrWhiteSpace(baseName) ? "Ny profil" : baseName.Trim();

        if (_profileStore.Profiles.All(profile => !string.Equals(profile.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;

        int suffix = 2;
        while (_profileStore.Profiles.Any(profile => string.Equals(profile.Name, $"{baseName} {suffix}", StringComparison.OrdinalIgnoreCase)))
            suffix++;

        return $"{baseName} {suffix}";
    }

    [RelayCommand]
    private void AddProfile()
    {
        var newProfile = new DeckProfile
        {
            Name = CreateUniqueProfileName("Ny profil")
        };
        newProfile.Initialize();

        _profileStore.Profiles.Add(newProfile);
        _profileStore.ActiveProfileId = newProfile.Id;

        _ = SwitchToProfileById(newProfile.Id);
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        DeckProfile duplicate;
        try
        {
            duplicate = CloneProfile(_profile);
        }
        catch
        {
            return;
        }

        duplicate.Id = Guid.NewGuid().ToString("N");
        duplicate.Name = CreateUniqueProfileName($"{_profile.Name} Copy");
        duplicate.Initialize();

        _profileStore.Profiles.Add(duplicate);
        _profileStore.ActiveProfileId = duplicate.Id;

        _ = SwitchToProfileById(duplicate.Id);
    }

    [RelayCommand]
    private void ImportProfile(DeckProfile? importedProfile)
    {
        if (importedProfile == null)
            return;

        ProfileSchemaMigrator.MigrateProfile(importedProfile);
        importedProfile.Id = Guid.NewGuid().ToString("N");

        string preferredName = string.IsNullOrWhiteSpace(importedProfile.Name)
            ? "Imported Profile"
            : importedProfile.Name.Trim();

        importedProfile.Name = CreateUniqueProfileName(preferredName);
        importedProfile.Initialize();

        _profileStore.Profiles.Add(importedProfile);
        _profileStore.ActiveProfileId = importedProfile.Id;

        _ = SwitchToProfileById(importedProfile.Id);
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (_profileStore.Profiles.Count <= 1)
            return;

        int removeIndex = GetActiveProfileIndex();
        string removedId = _profile.Id;

        _profileStore.Profiles.RemoveAll(profile => string.Equals(profile.Id, removedId, StringComparison.Ordinal));
        if (_profileStore.Profiles.Count == 0)
        {
            var fallbackProfile = new DeckProfile { Name = "Standard" };
            fallbackProfile.Initialize();
            _profileStore.Profiles.Add(fallbackProfile);
        }

        int nextIndex = Math.Clamp(removeIndex, 0, _profileStore.Profiles.Count - 1);
        _ = SwitchToProfileById(_profileStore.Profiles[nextIndex].Id);
    }

    [RelayCommand]
    private void RenameProfile(string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return;

        string trimmedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName)
            || string.Equals(_profile.Name, trimmedName, StringComparison.Ordinal))
        {
            return;
        }

        _profile.Name = trimmedName;
        RebuildProfileOptions();
        ScheduleAutoSave();
    }

    private bool SwitchToProfileById(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return false;

        if (string.Equals(_profile.Id, profileId, StringComparison.Ordinal))
            return true;

        var targetProfile = _profileStore.Profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal));
        if (targetProfile == null)
            return false;

        _profile = targetProfile;
        ProfileSchemaMigrator.MigrateProfile(_profile);
        _profileStore.ActiveProfileId = _profile.Id;

        SetVirtualLayoutIndex(-1);
        CurrentPageIndex = 0;
        CurrentNotePageIndex = Math.Clamp(_profile.CurrentNotePageIndex, 0, _profile.NotePages.Count - 1);
        StickyNotesVisible = true;

        RebuildLayoutTargets();
        LoadCurrentLayout();
        LoadQuickTextCategories();
        LoadQuickTextActionSteps();
        NotifyPageChanged();
        NotifyNotePageChanged();

        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(OverlayBackgroundColor));
        OnPropertyChanged(nameof(OverlayBackgroundImagePath));
        OnPropertyChanged(nameof(ButtonOverlayOpacity));
        OnPropertyChanged(nameof(ButtonSpacing));
        OnPropertyChanged(nameof(ButtonSize));
        OnPropertyChanged(nameof(StickyNoteFontSize));
        OnPropertyChanged(nameof(QuickTextSearchQuery));
        OnPropertyChanged(nameof(HasAnyQuickTextItems));
        OnPropertyChanged(nameof(GridOffsetX));
        OnPropertyChanged(nameof(GridOffsetY));
        OnPropertyChanged(nameof(QuickTextPanelX));
        OnPropertyChanged(nameof(QuickTextPanelY));
        OnPropertyChanged(nameof(QuickTextPanelWidth));
        OnPropertyChanged(nameof(QuickTextPanelHeight));
        OnPropertyChanged(nameof(MusicWidgetVisible));
        OnPropertyChanged(nameof(MusicWidgetMinimized));
        OnPropertyChanged(nameof(MusicWidgetX));
        OnPropertyChanged(nameof(MusicWidgetY));
        OnPropertyChanged(nameof(MusicWidgetWidth));
        OnPropertyChanged(nameof(MusicWidgetHeight));
        OnPropertyChanged(nameof(QuickTextFontSize));
        OnPropertyChanged(nameof(QuickTextPreviewLineHeight));
        OnPropertyChanged(nameof(QuickTextPreviewHeight));
        OnPropertyChanged(nameof(QuickTextEditorHeight));
        OnPropertyChanged(nameof(QuickTextHintLineHeight));
        OnPropertyChanged(nameof(QuickTextHintMaxHeight));
        OnPropertyChanged(nameof(HasQuickTextAction));
        OnPropertyChanged(nameof(HotkeyModifiers));
        OnPropertyChanged(nameof(HotkeyVk));
        OnPropertyChanged(nameof(HotkeyDisplayText));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(NaturalTypingEnabled));
        OnPropertyChanged(nameof(GamepadSupportEnabled));
        OnPropertyChanged(nameof(GamepadToggleButtons));
        OnPropertyChanged(nameof(GamepadToggleDisplayText));

        RebuildProfileOptions();
        SyncSelectedProfileId();
        if (IsOverlayOpen)
            ActivateMusicWidgetIfVisible();

        ScheduleAutoSave();
        return true;
    }

    private void RebuildProfileOptions()
    {
        ProfileOptions.Clear();

        for (int i = 0; i < _profileStore.Profiles.Count; i++)
        {
            var profile = _profileStore.Profiles[i];
            ProfileOptions.Add(new ProfileOption
            {
                Id = profile.Id,
                Label = profile.Name
            });
        }

        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(ProfileCount));
        OnPropertyChanged(nameof(CanRemoveProfile));
        OnPropertyChanged(nameof(ProfileIndicator));

        SyncSelectedProfileId();
    }

    private void SyncSelectedProfileId()
    {
        string currentId = _profile.Id;
        if (!string.Equals(SelectedProfileId, currentId, StringComparison.Ordinal))
            SelectedProfileId = currentId;
    }

    private static DeckProfile CloneProfile(DeckProfile source)
    {
        string json = JsonSerializer.Serialize(source, ProfileCloneJsonOptions);
        return JsonSerializer.Deserialize<DeckProfile>(json, ProfileCloneJsonOptions) ?? new DeckProfile();
    }
}
