namespace StreamDecky.Models;

/// <summary>Root of form-data.json: per-profile submissions and field history.</summary>
public class FormDataStore
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<FormProfileData> Profiles { get; set; } = new();

    public void Initialize()
    {
        Profiles ??= new List<FormProfileData>();
        foreach (var profile in Profiles)
            profile.EnsureInitialized();
    }

    public FormProfileData GetOrCreateProfileData(string profileId)
    {
        var data = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId, StringComparison.Ordinal));
        if (data == null)
        {
            data = new FormProfileData { ProfileId = profileId };
            Profiles.Add(data);
        }

        return data;
    }
}

public class FormProfileData
{
    public string ProfileId { get; set; } = string.Empty;
    /// <summary>Newest first.</summary>
    public List<FormSubmission> Submissions { get; set; } = new();
    /// <summary>Field id → previously submitted values, newest first. Keyed by
    /// field id so history survives key and label renames.</summary>
    public Dictionary<string, List<string>> FieldHistory { get; set; } = new();

    public void EnsureInitialized()
    {
        ProfileId ??= string.Empty;
        Submissions ??= new List<FormSubmission>();
        FieldHistory ??= new Dictionary<string, List<string>>();

        foreach (var submission in Submissions)
            submission.EnsureInitialized();

        foreach (var key in FieldHistory.Keys.ToList())
            FieldHistory[key] = (FieldHistory[key] ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
    }
}
