using System.Text.Json;
using DmxPro.Core.Serialization;

namespace DmxPro.Core.Projects;

/// <summary>Reads and writes <c>.dmxpro</c> project files (versioned JSON).</summary>
public sealed class ProjectSerializer
{
    public string Serialize(Project project) => JsonSerializer.Serialize(project, DmxJson.Options);

    public ProjectLoadResult Deserialize(string json)
    {
        int schemaVersion;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            schemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var version) && version.TryGetInt32(out var v) ? v : 0;
        }
        catch (JsonException ex)
        {
            throw new DmxFileFormatException($"Project file is damaged or not a DMX Pro project: {ex.Message}", ex);
        }

        if (schemaVersion < 1)
        {
            throw new DmxFileFormatException("Project file has no valid schemaVersion; it is not a DMX Pro project.");
        }

        if (schemaVersion > Project.CurrentSchemaVersion)
        {
            throw new DmxFileFormatException(
                $"This project was saved by a newer DMX Pro (format {schemaVersion}). Please update the app.");
        }

        // Future: run migrations from schemaVersion up to CurrentSchemaVersion here.
        Project? project;
        try
        {
            project = JsonSerializer.Deserialize<Project>(json, DmxJson.Options);
        }
        catch (JsonException ex)
        {
            throw new DmxFileFormatException($"Project file is damaged: {ex.Message}", ex);
        }

        if (project is null)
        {
            throw new DmxFileFormatException("Project file is empty.");
        }

        project.SchemaVersion = Project.CurrentSchemaVersion;
        return new ProjectLoadResult(project, project.Validate());
    }

    public async Task<ProjectLoadResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return Deserialize(json);
    }

    /// <summary>
    /// Saves atomically: writes a temp file, keeps the previous version as <c>.bak</c>, then swaps. A crash mid-save
    /// never leaves a half-written project.
    /// </summary>
    public async Task SaveAsync(Project project, string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = fullPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, Serialize(project), cancellationToken).ConfigureAwait(false);

        if (File.Exists(fullPath))
        {
            File.Copy(fullPath, fullPath + ".bak", overwrite: true);
        }

        File.Move(tempPath, fullPath, overwrite: true);
    }
}
