using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DmxPro.App.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in <c>%APPDATA%\DmxPro\settings.json</c>. Never throws on a
/// missing or corrupt file: it falls back to defaults, keeps a copy of the bad file and reports a warning.
/// </summary>
public sealed class AppSettingsService
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(ILogger<AppSettingsService> logger, string? filePath = null)
    {
        _logger = logger;
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DmxPro", FileName);
        Current = Load();
    }

    /// <summary>Raised after the settings changed and were saved (on the caller's thread).</summary>
    public event EventHandler? Changed;

    public string FilePath { get; }

    public AppSettings Current { get; private set; }

    /// <summary>Set when the file could not be read at startup (defaults are used); shown on the Settings page.</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>Set when the last save failed; cleared by the next successful save.</summary>
    public string? SaveError { get; private set; }

    /// <summary>Applies <paramref name="change"/>, repairs out-of-range values, saves and raises <see cref="Changed"/>.</summary>
    public bool Update(Action<AppSettings> change)
    {
        change(Current);
        Current.Normalize();
        var saved = Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <summary>
    /// Restores every preference to its default. Output configurations, the sACN identity and the recent-projects list
    /// (history, not a preference) are kept.
    /// </summary>
    public bool ResetToDefaults()
    {
        Current = new AppSettings
        {
            SacnCid = Current.SacnCid,
            Outputs = Current.Outputs,
            RecentProjects = Current.RecentProjects,
        };
        var saved = Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <summary>Writes the file atomically (temp file + move). Returns false and sets <see cref="SaveError"/> on failure.</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
            SaveError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            _logger.LogWarning("Could not save settings to {Path}: {Error}", FilePath, ex.Message);
            SaveError = $"Settings could not be saved: {ex.Message}";
            return false;
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
                           ?? throw new JsonException("The file is empty.");
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            _logger.LogWarning("Settings file {Path} could not be read, using defaults: {Error}", FilePath, ex.Message);
            var backup = BackUpBadFile();
            LoadWarning = "Your settings file could not be read, so default settings are used." +
                          (backup is null ? string.Empty : $" The old file was kept as {Path.GetFileName(backup)}.") +
                          $" ({ex.Message})";
            return new AppSettings();
        }
    }

    /// <summary>Keeps the unreadable file so the next save does not silently destroy it.</summary>
    private string? BackUpBadFile()
    {
        try
        {
            var backup = Path.ChangeExtension(FilePath, ".corrupt.json");
            File.Copy(FilePath, backup, overwrite: true);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not back up the unreadable settings file: {Error}", ex.Message);
            return null;
        }
    }
}
