namespace StreamDecky.Models;

public class DeckProfileStore
{
    public int SchemaVersion { get; set; }
    public string ActiveProfileId { get; set; } = string.Empty;
    public List<DeckProfile> Profiles { get; set; } = new();

    public void Initialize()
    {
        Profiles ??= new List<DeckProfile>();

        if (Profiles.Count == 0)
            Profiles.Add(new DeckProfile { Name = "Standard" });

        foreach (var profile in Profiles)
        {
            profile.Initialize();
            if (string.IsNullOrWhiteSpace(profile.Name))
                profile.Name = "Standard";
        }

        if (string.IsNullOrWhiteSpace(ActiveProfileId)
            || !Profiles.Any(profile => string.Equals(profile.Id, ActiveProfileId, StringComparison.Ordinal)))
        {
            ActiveProfileId = Profiles[0].Id;
        }
    }

    public DeckProfile GetActiveProfile()
    {
        Initialize();
        return Profiles.First(profile => string.Equals(profile.Id, ActiveProfileId, StringComparison.Ordinal));
    }
}
