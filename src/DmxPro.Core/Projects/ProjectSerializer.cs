using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DmxPro.Core.Functions;
using DmxPro.Core.Serialization;

namespace DmxPro.Core.Projects;

/// <summary>Reads and writes <c>.dmxpro</c> project files (versioned JSON).</summary>
public sealed class ProjectSerializer
{
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public string Serialize(Project project) => JsonSerializer.Serialize(project, DmxJson.Options);

    public ProjectLoadResult Deserialize(string json) => Deserialize(Encoding.UTF8.GetBytes(json));

    /// <summary>
    /// Reads a project from UTF-8 JSON (a file's bytes). Works on the bytes directly: no UTF-16 copy and no throwaway
    /// document tree, which matters for big show projects.
    /// </summary>
    public ProjectLoadResult Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.StartsWith(Utf8Bom))
        {
            utf8Json = utf8Json[Utf8Bom.Length..];
        }

        var schemaVersion = ReadSchemaVersion(utf8Json);
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
            project = JsonSerializer.Deserialize<Project>(utf8Json, DmxJson.Options);
        }
        catch (JsonException ex)
        {
            throw new DmxFileFormatException($"Project file is damaged: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            // e.g. a function without its "type" discriminator (abstract type cannot be created).
            throw new DmxFileFormatException($"Project file is damaged: {ex.Message}", ex);
        }

        if (project is null)
        {
            throw new DmxFileFormatException("Project file is empty.");
        }

        project.SchemaVersion = Project.CurrentSchemaVersion;
        ShareChannelKeys(project);
        RepairStage(project);
        foreach (var function in project.Functions)
        {
            function.ClampTimes();
        }

        // Sequences were merged into Chaser (2026-10-27): convert after the times are clamped, so the chaser steps get
        // the repaired timing. The next save writes chasers only.
        SequenceMigration.Migrate(project);

        // Colour cycle, fade, pulse, flash and strobe were retired (2026-10-28): they become scenes and chasers with the
        // same ids, so everything that points at them keeps working.
        LegacyFunctionMigration.Migrate(project);

        foreach (var show in project.Shows)
        {
            show.NormalizeAudio();
        }

        return new ProjectLoadResult(project, project.Validate());
    }

    /// <summary>
    /// A fingerprint of a project file's exact bytes (SHA-256, hex). The app compares it with the file on disk to tell its
    /// own saves from changes made by another program.
    /// </summary>
    public static string ComputeContentHash(ReadOnlySpan<byte> fileBytes) => Convert.ToHexString(SHA256.HashData(fileBytes));

    /// <summary>
    /// Reads and parses the file off the calling thread (after the first await). The result's
    /// <see cref="ProjectLoadResult.ContentHash"/> is the hash of the bytes read.
    /// </summary>
    public async Task<ProjectLoadResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Deserialize(bytes) with { ContentHash = ComputeContentHash(bytes) };
    }

    /// <summary>
    /// Saves atomically: writes a temp file, keeps the previous version as <c>.bak</c>, then swaps. A crash mid-save
    /// never leaves a half-written project. Returns the <see cref="ComputeContentHash"/> of the bytes written.
    /// </summary>
    public async Task<string> SaveAsync(Project project, string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Serialized on the calling thread (the project is edited there), straight to UTF-8: half the memory of a
        // UTF-16 string for big projects.
        var tempPath = fullPath + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, DmxJson.Options);
        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);

        if (File.Exists(fullPath))
        {
            File.Copy(fullPath, fullPath + ".bak", overwrite: true);
        }

        File.Move(tempPath, fullPath, overwrite: true);
        return ComputeContentHash(bytes);
    }

    /// <summary>
    /// The top-level <c>schemaVersion</c>, or 0 when missing or not an integer. Reads the whole document once, so damaged
    /// JSON is reported here, before deserializing.
    /// </summary>
    private static int ReadSchemaVersion(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        try
        {
            var version = 0;
            if (!reader.Read())
            {
                return 0;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                ReadToEnd(ref reader);
                return 0;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isVersion = reader.ValueTextEquals("schemaVersion"u8);
                reader.Read();
                if (isVersion)
                {
                    version = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var v) ? v : 0;
                }

                reader.Skip();
            }

            ReadToEnd(ref reader);
            return version;
        }
        catch (JsonException ex)
        {
            throw new DmxFileFormatException($"Project file is damaged or not a DMX Pro project: {ex.Message}", ex);
        }

        // Anything after the root value other than whitespace/comments is an error (the reader throws).
        static void ReadToEnd(ref Utf8JsonReader reader)
        {
            while (reader.Read())
            {
            }
        }
    }

    /// <summary>
    /// A hand-edited or older file loads and is corrected rather than rejected: positions are pulled onto the stage,
    /// duplicates dropped, and placements of fixtures that no longer exist removed so the next save is clean.
    /// </summary>
    private static void RepairStage(Project project)
    {
        project.Stage.Normalize();
        var fixtureIds = new HashSet<Guid>(project.Fixtures.Count);
        foreach (var fixture in project.Fixtures)
        {
            fixtureIds.Add(fixture.Id);
        }

        project.Stage.RemoveMissing(fixtureIds);
    }

    /// <summary>
    /// Every scene value read from JSON gets its own channel-key string ("red" thousands of times in a big show
    /// project). Point equal keys at one instance — preferably the profile's own key — to save memory.
    /// </summary>
    private static void ShareChannelKeys(Project project)
    {
        var pool = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var profile in project.Profiles)
        {
            foreach (var channel in profile.Channels)
            {
                if (channel.Key is { } key)
                {
                    pool.TryAdd(key, key);
                }
            }
        }

        foreach (var function in project.Functions)
        {
            switch (function)
            {
                case SceneFunction scene:
                    Share(scene.Values, pool);
                    break;

                case SequenceFunction sequence:
                    foreach (var step in sequence.Steps)
                    {
                        Share(step.Values, pool);
                    }

                    break;
            }
        }

        static void Share(List<SceneValue> values, Dictionary<string, string> pool)
        {
            foreach (var value in values)
            {
                if (value.ChannelKey is not { } key)
                {
                    continue;
                }

                if (pool.TryGetValue(key, out var shared))
                {
                    value.ChannelKey = shared;
                }
                else
                {
                    pool.Add(key, key);
                }
            }
        }
    }
}
