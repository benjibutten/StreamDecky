using System.IO;
using System.Text.Json;
using StreamDecky.Helpers;
using StreamDecky.Models;

namespace StreamDecky.Services;

/// <summary>
/// Persists form submissions and per-field autocomplete history in
/// %LOCALAPPDATA%\StreamDecky\form-data.json. Kept separate from profiles.json
/// because submissions mutate on every fill-in and should neither churn the
/// profile backup chain nor travel along with profile exports.
/// </summary>
public class FormDataService
{
    public const int MaxSubmissionsPerProfile = 300;
    public const int MaxHistoryPerField = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _appDataFolder;
    private readonly string _dataPath;
    private FormDataStore? _store;
    private bool _persistenceBlocked;

    public FormDataService(string? appDataFolder = null)
    {
        _appDataFolder = string.IsNullOrWhiteSpace(appDataFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamDecky")
            : appDataFolder;

        _dataPath = Path.Combine(_appDataFolder, "form-data.json");
    }

    private FormDataStore Store => _store ??= Load();

    private FormDataStore Load()
    {
        if (!File.Exists(_dataPath))
            return new FormDataStore();

        try
        {
            var json = File.ReadAllText(_dataPath);
            var store = JsonSerializer.Deserialize<FormDataStore>(json, JsonOptions) ?? new FormDataStore();
            if (store.SchemaVersion > FormDataStore.CurrentSchemaVersion)
            {
                _persistenceBlocked = true;
                AppDiagnostics.Warning(
                    $"Form data schema version {store.SchemaVersion} is newer than supported version {FormDataStore.CurrentSchemaVersion}. "
                    + "The data will load, but saving is blocked to avoid data loss.");
            }
            store.Initialize();
            return store;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to load form data '{_dataPath}'. Starting with an empty store.", ex);
            return new FormDataStore();
        }
    }

    public IReadOnlyList<FormSubmission> GetSubmissions(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return Array.Empty<FormSubmission>();

        return Store.Profiles
            .FirstOrDefault(profile => string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal))
            ?.Submissions ?? (IReadOnlyList<FormSubmission>)Array.Empty<FormSubmission>();
    }

    public IReadOnlyList<string> GetFieldHistory(string profileId, string fieldId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(fieldId))
            return Array.Empty<string>();

        var profileData = Store.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal));
        if (profileData == null || !profileData.FieldHistory.TryGetValue(fieldId, out var history))
            return Array.Empty<string>();

        return history;
    }

    public bool RecordSubmission(
        string profileId,
        FormSubmission submission,
        IEnumerable<KeyValuePair<string, string>>? historyEntries = null)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (string.IsNullOrWhiteSpace(profileId))
            return false;

        submission.EnsureInitialized();

        var profileData = Store.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal));
        bool createdProfileData = profileData == null;
        profileData ??= Store.GetOrCreateProfileData(profileId);
        var previousSubmissions = profileData.Submissions.ToList();
        var previousHistory = profileData.FieldHistory.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList());

        profileData.Submissions.Insert(0, submission);
        while (profileData.Submissions.Count > MaxSubmissionsPerProfile)
            profileData.Submissions.RemoveAt(profileData.Submissions.Count - 1);

        if (historyEntries != null)
        {
            foreach (var (fieldId, value) in historyEntries)
                AddFieldHistory(profileData, fieldId, value);
        }

        if (Save())
            return true;

        if (createdProfileData)
        {
            Store.Profiles.Remove(profileData);
        }
        else
        {
            profileData.Submissions.Clear();
            profileData.Submissions.AddRange(previousSubmissions);
            profileData.FieldHistory.Clear();
            foreach (var (fieldId, values) in previousHistory)
                profileData.FieldHistory[fieldId] = values;
        }

        return false;
    }

    public bool SetSubmissionCompleted(string profileId, string submissionId, bool completed)
    {
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(submissionId))
            return false;

        var submission = Store.Profiles
            .FirstOrDefault(profile => string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal))
            ?.Submissions.FirstOrDefault(candidate => string.Equals(candidate.Id, submissionId, StringComparison.Ordinal));
        if (submission == null || submission.IsCompleted == completed)
            return false;

        bool previousCompleted = submission.IsCompleted;
        submission.IsCompleted = completed;
        if (Save())
            return true;

        submission.IsCompleted = previousCompleted;
        return false;
    }

    public bool UpdateSubmissionField(
        string profileId,
        string submissionId,
        string fieldLabel,
        string value,
        string? stableFieldId = null)
    {
        if (string.IsNullOrWhiteSpace(profileId)
            || string.IsNullOrWhiteSpace(submissionId)
            || string.IsNullOrWhiteSpace(fieldLabel))
        {
            return false;
        }

        var submission = Store.Profiles
            .FirstOrDefault(profile => string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal))
            ?.Submissions.FirstOrDefault(candidate => string.Equals(candidate.Id, submissionId, StringComparison.Ordinal));
        if (submission == null
            || !submission.Values.TryGetValue(fieldLabel, out string? previousValue)
            || string.Equals(previousValue, value, StringComparison.Ordinal))
        {
            return false;
        }

        value ??= string.Empty;
        var profileData = Store.Profiles.First(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal));
        submission.FieldIds.TryGetValue(fieldLabel, out string? storedFieldId);
        string? fieldId = !string.IsNullOrWhiteSpace(storedFieldId) ? storedFieldId : stableFieldId;

        var targets = new List<(FormSubmission Submission, string Label)>();
        if (!string.IsNullOrWhiteSpace(fieldId))
        {
            foreach (var candidate in profileData.Submissions)
            {
                bool foundStableMapping = false;
                foreach (var (candidateLabel, candidateFieldId) in candidate.FieldIds)
                {
                    if (string.Equals(candidateFieldId, fieldId, StringComparison.Ordinal)
                        && candidate.Values.TryGetValue(candidateLabel, out string? candidateValue)
                        && string.Equals(candidateValue, previousValue, StringComparison.OrdinalIgnoreCase))
                    {
                        targets.Add((candidate, candidateLabel));
                        foundStableMapping = true;
                    }
                }

                // Compatibility for submissions created before stable field ids
                // were stored: same template and label identifies the field.
                if (!foundStableMapping
                    && string.Equals(candidate.TemplateId, submission.TemplateId, StringComparison.Ordinal)
                    && candidate.Values.TryGetValue(fieldLabel, out string? legacyValue)
                    && string.Equals(legacyValue, previousValue, StringComparison.OrdinalIgnoreCase))
                {
                    targets.Add((candidate, fieldLabel));
                }
            }
        }

        if (targets.Count == 0)
            targets.Add((submission, fieldLabel));

        var previousStates = targets.Select(target =>
            CaptureSubmissionFieldState(target.Submission, target.Label)).ToList();

        foreach (var target in targets)
        {
            if (!string.IsNullOrWhiteSpace(fieldId))
                target.Submission.FieldIds[target.Label] = fieldId;
            ApplySubmissionFieldValue(target.Submission, target.Label, value);
        }

        List<string>? previousHistory = null;
        if (!string.IsNullOrWhiteSpace(fieldId)
            && profileData.FieldHistory.TryGetValue(fieldId, out var history))
        {
            previousHistory = history.ToList();
            history.RemoveAll(entry =>
                string.Equals(entry, previousValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry, value, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(value))
                history.Insert(0, value);
        }

        if (Save())
            return true;

        foreach (var state in previousStates)
        {
            state.Submission.Values[state.Label] = state.Value;
            state.Submission.RenderedText = state.RenderedText;
            if (state.HadFieldId)
                state.Submission.FieldIds[state.Label] = state.FieldId!;
            else
                state.Submission.FieldIds.Remove(state.Label);
            if (state.Token == null)
                continue;

            if (state.HadTokenValue)
                state.Submission.TokenValues[state.Token] = state.TokenValue!;
            else
                state.Submission.TokenValues.Remove(state.Token);
        }

        if (previousHistory != null)
        {
            var restoredHistory = profileData.FieldHistory[fieldId!];
            restoredHistory.Clear();
            restoredHistory.AddRange(previousHistory);
        }

        return false;
    }

    private static void ApplySubmissionFieldValue(FormSubmission submission, string fieldLabel, string value)
    {
        submission.Values[fieldLabel] = value;
        if (!submission.FieldTokens.TryGetValue(fieldLabel, out string? fieldToken)
            || string.IsNullOrWhiteSpace(fieldToken)
            || string.IsNullOrWhiteSpace(submission.OutputTemplateSnapshot))
        {
            return;
        }

        submission.TokenValues[fieldToken] = value;
        submission.RenderedText = FormRenderService.RenderTemplate(
            submission.OutputTemplateSnapshot,
            submission.TokenValues);
    }

    private static SubmissionFieldState CaptureSubmissionFieldState(FormSubmission submission, string fieldLabel)
    {
        string? token = submission.FieldTokens.TryGetValue(fieldLabel, out string? fieldToken)
            ? fieldToken
            : null;
        string? tokenValue = null;
        bool hadTokenValue = token != null && submission.TokenValues.TryGetValue(token, out tokenValue);
        bool hadFieldId = submission.FieldIds.TryGetValue(fieldLabel, out string? fieldId);
        return new SubmissionFieldState(
            submission,
            fieldLabel,
            submission.Values[fieldLabel],
            submission.RenderedText,
            token,
            hadTokenValue,
            tokenValue,
            hadFieldId,
            fieldId);
    }

    private sealed record SubmissionFieldState(
        FormSubmission Submission,
        string Label,
        string Value,
        string RenderedText,
        string? Token,
        bool HadTokenValue,
        string? TokenValue,
        bool HadFieldId,
        string? FieldId);

    public bool DeleteSubmission(string profileId, string submissionId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(submissionId))
            return false;

        var profileData = Store.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal));
        if (profileData == null)
            return false;

        var previousSubmissions = profileData.Submissions.ToList();
        int removed = profileData.Submissions.RemoveAll(submission =>
            string.Equals(submission.Id, submissionId, StringComparison.Ordinal));
        if (removed == 0)
            return false;

        if (Save())
            return true;

        profileData.Submissions.Clear();
        profileData.Submissions.AddRange(previousSubmissions);
        return false;
    }

    public int ClearSubmissions(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return 0;

        var profileData = Store.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal));
        if (profileData == null || profileData.Submissions.Count == 0)
            return 0;

        var previousSubmissions = profileData.Submissions.ToList();
        int removed = profileData.Submissions.Count;
        profileData.Submissions.Clear();
        if (Save())
            return removed;

        profileData.Submissions.AddRange(previousSubmissions);
        return 0;
    }

    private static void AddFieldHistory(FormProfileData profileData, string fieldId, string value)
    {
        if (string.IsNullOrWhiteSpace(fieldId) || string.IsNullOrWhiteSpace(value))
            return;

        string trimmed = value.Trim();
        if (!profileData.FieldHistory.TryGetValue(fieldId, out var history))
        {
            history = new List<string>();
            profileData.FieldHistory[fieldId] = history;
        }

        history.RemoveAll(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase));
        history.Insert(0, trimmed);
        while (history.Count > MaxHistoryPerField)
            history.RemoveAt(history.Count - 1);
    }

    private bool Save()
    {
        if (_persistenceBlocked)
        {
            AppDiagnostics.Warning($"Saving form data '{_dataPath}' was blocked because its schema is newer than this app supports.");
            return false;
        }

        try
        {
            Directory.CreateDirectory(_appDataFolder);
            var json = JsonSerializer.Serialize(Store, JsonOptions);
            string tempPath = Path.Combine(_appDataFolder, $"form-data.json.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, json);

            if (File.Exists(_dataPath))
            {
                string backupPath = tempPath + ".bak";
                try
                {
                    File.Replace(tempPath, _dataPath, backupPath, ignoreMetadataErrors: true);
                }
                finally
                {
                    TryDeleteFile(backupPath);
                }
            }
            else
            {
                File.Move(tempPath, _dataPath);
            }

            TryDeleteFile(tempPath);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Warning($"Failed to save form data '{_dataPath}'.", ex);
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup failures should not mask the original result.
        }
    }
}
