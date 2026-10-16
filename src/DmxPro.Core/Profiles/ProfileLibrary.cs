using DmxPro.Core.Validation;

namespace DmxPro.Core.Profiles;

/// <summary>
/// The set of fixture profiles available to pick from (built-in, user folder, imported). Projects embed copies of the
/// profiles they use, so a project stays openable even if the library changes.
/// </summary>
public sealed class ProfileLibrary : IProfileResolver
{
    private readonly Dictionary<string, FixtureProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FixtureProfile> Profiles =>
        _profiles.Values.OrderBy(p => p.Manufacturer).ThenBy(p => p.Model).ToList();

    public FixtureProfile? FindProfile(string profileId) => _profiles.GetValueOrDefault(profileId);

    /// <summary>Adds or replaces a profile. Invalid profiles are rejected with their issues.</summary>
    public IReadOnlyList<ValidationIssue> Add(FixtureProfile profile)
    {
        var issues = profile.Validate();
        if (issues.All(i => i.Severity != ValidationSeverity.Error))
        {
            _profiles[profile.Id] = profile;
        }

        return issues;
    }

    public bool Remove(string profileId) => _profiles.Remove(profileId);

    /// <summary>
    /// Loads every <c>*.dmxprofile.json</c> and <c>*.qxf</c> file in <paramref name="directory"/>.
    /// Bad files are reported, never thrown, so one broken profile cannot stop the app from starting.
    /// </summary>
    public async Task<IReadOnlyList<ValidationIssue>> LoadDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        if (!Directory.Exists(directory))
        {
            return issues;
        }

        foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            try
            {
                FixtureProfile profile;
                if (fileName.EndsWith(ProfileSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    profile = await ProfileSerializer.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                }
                else if (fileName.EndsWith(QxfImporter.FileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    profile = QxfImporter.ImportFile(path);
                }
                else
                {
                    continue;
                }

                issues.AddRange(Add(profile).Select(i => i with { Message = $"{fileName}: {i.Message}" }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Serialization.DmxFileFormatException)
            {
                issues.Add(ValidationIssue.Error("PROFILE_LOAD_FAILED", $"{fileName}: {ex.Message}"));
            }
        }

        return issues;
    }
}
