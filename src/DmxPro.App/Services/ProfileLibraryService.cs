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
    /// Saves a user-defined channel layout (<see cref="ChannelLayout.IsCustom"/>) as <c>&lt;id&gt;.dmxprofile.json</c>
    /// in the user profile folder (written to a temp file first, then moved over the old file, so a failed write never
    /// leaves a half-written profile), then adds it to <see cref="Library"/> and raises <see cref="LibraryChanged"/>.
    /// Problems are returned, never thrown; an empty list or warnings only means it was saved.
    /// </summary>
    public async Task<IReadOnlyList<ValidationIssue>> SaveUserProfileAsync(FixtureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!ChannelLayout.IsCustom(profile))
        {
            return [ValidationIssue.Error("PROFILE_READ_ONLY", $"'{profile.DisplayName}' is a built-in or imported profile and cannot be changed.")];
        }

        if (Library.FindProfile(profile.Id) is { } known && !ChannelLayout.IsCustom(known))
        {
            return [ValidationIssue.Error("PROFILE_ID_TAKEN", $"The id '{profile.Id}' belongs to a built-in or imported profile.")];
        }

        if (!IsSafeFileName(profile.Id))
        {
            return [ValidationIssue.Error("PROFILE_ID_INVALID", $"The profile id '{profile.Id}' cannot be used as a file name.")];
        }

        var issues = profile.Validate();
        if (issues.Any(i => i.Severity == ValidationSeverity.Error))
        {
            return issues;
        }

        var target = UserProfilePath(profile.Id);
        var temp = target + ".tmp";
        try
        {
            Directory.CreateDirectory(_paths.UserProfilesDirectory);
            await ProfileSerializer.SaveAsync(profile, temp);
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return [.. issues, ValidationIssue.Error("PROFILE_SAVE_FAILED", $"Could not save '{profile.DisplayName}': {ex.Message}")];
        }

        Library.Add(profile.Clone());
        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return issues;
    }

    /// <summary>
    /// Deletes a user-defined channel layout file and removes it from <see cref="Library"/>. Built-in and imported
    /// profiles are refused. Projects keep their embedded copy, so patched fixtures keep working.
    /// </summary>
    public Task<IReadOnlyList<ValidationIssue>> DeleteUserProfileAsync(string profileId)
    {
        IReadOnlyList<ValidationIssue> result;
        var profile = Library.FindProfile(profileId);
        if (profile is null)
        {
            result = [ValidationIssue.Error("PROFILE_NOT_FOUND", $"Profile '{profileId}' is not in the library.")];
        }
        else if (!ChannelLayout.IsCustom(profile) || !IsSafeFileName(profile.Id))
        {
            result = [ValidationIssue.Error("PROFILE_READ_ONLY", $"'{profile.DisplayName}' is a built-in or imported profile and cannot be deleted.")];
        }
        else
        {
            try
            {
                File.Delete(UserProfilePath(profile.Id));
                Library.Remove(profile.Id);
                LibraryChanged?.Invoke(this, EventArgs.Empty);
                result = [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result = [ValidationIssue.Error("PROFILE_DELETE_FAILED", $"Could not delete '{profile.DisplayName}': {ex.Message}")];
            }
        }

        return Task.FromResult(result);
    }

    private string UserProfilePath(string profileId) =>
        Path.Combine(_paths.UserProfilesDirectory, profileId + ProfileSerializer.FileExtension);

    private static bool IsSafeFileName(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !id.Contains("..", StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a stale .tmp file is ignored by the loader (wrong extension) and overwritten next save.
        }
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
