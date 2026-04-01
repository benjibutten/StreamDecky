using System.IO;
using System.Text.Json;
using StreamDecky.Models;

namespace StreamDecky.Services;

public class ProfileService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StreamDecky");

    private static readonly string ProfilePath = Path.Combine(AppDataFolder, "profile.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public DeckProfile Load()
    {
        if (!File.Exists(ProfilePath))
        {
            var profile = new DeckProfile();
            profile.Initialize();
            return profile;
        }

        try
        {
            var json = File.ReadAllText(ProfilePath);
            var profile = JsonSerializer.Deserialize<DeckProfile>(json, JsonOptions) ?? new DeckProfile();
            profile.Initialize();
            return profile;
        }
        catch
        {
            var profile = new DeckProfile();
            profile.Initialize();
            return profile;
        }
    }

    public void Save(DeckProfile profile)
    {
        Directory.CreateDirectory(AppDataFolder);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(ProfilePath, json);
    }
}
