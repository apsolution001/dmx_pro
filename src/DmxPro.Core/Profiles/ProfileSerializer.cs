using System.Text.Json;
using DmxPro.Core.Serialization;

namespace DmxPro.Core.Profiles;

/// <summary>Reads and writes <c>*.dmxprofile.json</c> files.</summary>
public static class ProfileSerializer
{
    public const string FileExtension = ".dmxprofile.json";

    public static string Serialize(FixtureProfile profile) => JsonSerializer.Serialize(profile, DmxJson.Options);

    public static FixtureProfile Deserialize(string json)
    {
        FixtureProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<FixtureProfile>(json, DmxJson.Options);
        }
        catch (JsonException ex)
        {
            throw new DmxFileFormatException($"Profile file is not valid JSON: {ex.Message}", ex);
        }

        if (profile is null)
        {
            throw new DmxFileFormatException("Profile file is empty.");
        }

        if (profile.SchemaVersion > FixtureProfile.CurrentSchemaVersion)
        {
            throw new DmxFileFormatException(
                $"Profile '{profile.Id}' uses format version {profile.SchemaVersion}; this app supports up to {FixtureProfile.CurrentSchemaVersion}.");
        }

        return profile;
    }

    public static async Task<FixtureProfile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return Deserialize(json);
    }

    public static Task SaveAsync(FixtureProfile profile, string path, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, Serialize(profile), cancellationToken);
}
