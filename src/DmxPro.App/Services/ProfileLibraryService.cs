using DmxPro.Core.Profiles;
using DmxPro.Core.Validation;

namespace DmxPro.App.Services;

/// <summary>
/// Loads built-in and user profiles into one <see cref="ProfileLibrary"/> and imports new profile files into the
/// user profile folder.
/// </summary>
public sealed class ProfileLibraryService
{
    private readonly AppPaths _paths;

    public ProfileLibraryService(AppPaths paths)
    {
        _paths = paths;
    }

    public event EventHandler? LibraryChanged;

    public ProfileLibrary Library { get; private set; } = new();

    public async Task<IReadOnlyList<ValidationIssue>> ReloadAsync()
    {
        var library = new ProfileLibrary();
        var issues = new List<ValidationIssue>();
        issues.AddRange(await library.LoadDirectoryAsync(_paths.BuiltInProfilesDirectory));
        issues.AddRange(await library.LoadDirectoryAsync(_paths.UserProfilesDirectory));
        Library = library;
        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return issues;
    }

    /// <summary>
    /// Imports <c>.qxf</c> or <c>.dmxprofile.json</c> files: converts them, saves them as DMX Pro profiles in the user
    /// folder, and adds them to the library. Returns imported profiles and any problems.
    /// </summary>
    public async Task<(IReadOnlyList<FixtureProfile> Imported, IReadOnlyList<ValidationIssue> Issues)> ImportAsync(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(_paths.UserProfilesDirectory);
        var imported = new List<FixtureProfile>();
        var issues = new List<ValidationIssue>();

        foreach (var path in paths)
        {
            var fileName = Path.GetFileName(path);
            try
            {
                var profile = path.EndsWith(QxfImporter.FileExtension, StringComparison.OrdinalIgnoreCase)
                    ? QxfImporter.ImportFile(path)
                    : await ProfileSerializer.LoadAsync(path);

                var profileIssues = profile.Validate();
                issues.AddRange(profileIssues.Select(i => i with { Message = $"{fileName}: {i.Message}" }));
                if (profileIssues.Any(i => i.Severity == ValidationSeverity.Error))
                {
                    continue;
                }

                var target = Path.Combine(_paths.UserProfilesDirectory, profile.Id + ProfileSerializer.FileExtension);
                await ProfileSerializer.SaveAsync(profile, target);
                Library.Add(profile);
                imported.Add(profile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Core.Serialization.DmxFileFormatException)
            {
                issues.Add(ValidationIssue.Error("PROFILE_IMPORT_FAILED", $"{fileName}: {ex.Message}"));
            }
        }

        if (imported.Count > 0)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }

        return (imported, issues);
    }
}
