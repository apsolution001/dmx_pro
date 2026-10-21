using DmxPro.Core.Projects;
using DmxPro.Output.Configuration;

namespace DmxPro.App.Services;

/// <summary>
/// App-level preferences, saved as JSON in <c>%APPDATA%\DmxPro\settings.json</c> (not in the project file).
/// There is deliberately no "output enabled" flag: output starts disabled on every app start (safety rule 1).
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MinStrobeRateHz = 1;
    public const int MaxStrobeRateLimitHz = 25;
    public const int DefaultMaxStrobeRateHz = 10;
    public const int MinSeekStepSeconds = 1;
    public const int MaxSeekStepSeconds = 60;
    public const int DefaultSeekStepSeconds = 5;
    public const int DefaultDmxRefreshRateHz = 44;
    public const int MaxAutosaveIntervalMinutes = 120;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // ── Appearance ──
    public ThemeModeSetting Theme { get; set; } = ThemeModeSetting.Dark;

    // ── Safety ──
    public SafeFrameSetting SafeFrameOnExit { get; set; } = SafeFrameSetting.Blackout;

    /// <summary>Global strobe switch (safety rule 8). When off, strobe channels/effects stay at their "no strobe" value.</summary>
    public bool AllowStrobe { get; set; }

    /// <summary>Upper limit for any strobe effect or strobe channel rate, in flashes per second.</summary>
    public int MaxStrobeRateHz { get; set; } = DefaultMaxStrobeRateHz;

    /// <summary>Ask before Release All (off by default: Release All is a panic button).</summary>
    public bool ConfirmReleaseAll { get; set; }

    // ── Show playback ──
    public ShowOverrideModeSetting ConsoleOverrideMode { get; set; } = ShowOverrideModeSetting.MuteWholeShow;

    /// <summary>Seconds the show transport jumps on back / forward.</summary>
    public int SeekStepSeconds { get; set; } = DefaultSeekStepSeconds;

    // ── Engine ──
    /// <summary>Engine tick / DMX refresh rate. Shown read-only for now.</summary>
    public int DmxRefreshRateHz { get; set; } = DefaultDmxRefreshRateHz;

    // ── Project ──
    /// <summary>Minutes between autosaves; 0 = off. The autosave feature itself arrives in phase 9.</summary>
    public int AutosaveIntervalMinutes { get; set; }

    /// <summary>Folder for Open / Save As dialogs; null = <c>Documents\DmxPro</c>.</summary>
    public string? DefaultProjectFolder { get; set; }

    /// <summary>Extra folders scanned for fixture profiles (besides the built-in and user folders).</summary>
    public List<string> ProfileFolders { get; set; } = [];

    /// <summary>File ▸ Open recent: last opened or saved project files, newest first (see <see cref="RecentProjectList"/>).</summary>
    public List<string> RecentProjects { get; set; } = [];

    // ── Outputs (Input / Output page) ──
    /// <summary>Per-install sACN component identifier; must stay stable so receivers recognise this source.</summary>
    public Guid SacnCid { get; set; } = Guid.NewGuid();

    /// <summary>Output configurations, one per configured universe. Never contains an enabled state.</summary>
    public List<OutputConfig> Outputs { get; set; } = [];

    /// <summary>Repairs values a hand-edited or older file may contain (nulls, out-of-range numbers).</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Theme))
        {
            Theme = ThemeModeSetting.Dark;
        }

        if (!Enum.IsDefined(SafeFrameOnExit))
        {
            SafeFrameOnExit = SafeFrameSetting.Blackout;
        }

        if (!Enum.IsDefined(ConsoleOverrideMode))
        {
            ConsoleOverrideMode = ShowOverrideModeSetting.MuteWholeShow;
        }

        MaxStrobeRateHz = Math.Clamp(MaxStrobeRateHz, MinStrobeRateHz, MaxStrobeRateLimitHz);
        SeekStepSeconds = Math.Clamp(SeekStepSeconds, MinSeekStepSeconds, MaxSeekStepSeconds);
        DmxRefreshRateHz = Math.Clamp(DmxRefreshRateHz, OutputConfig.MinFrameRateHz, OutputConfig.MaxFrameRateHz);
        AutosaveIntervalMinutes = Math.Clamp(AutosaveIntervalMinutes, 0, MaxAutosaveIntervalMinutes);
        if (string.IsNullOrWhiteSpace(DefaultProjectFolder))
        {
            DefaultProjectFolder = null;
        }

        ProfileFolders = (ProfileFolders ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        RecentProjects = RecentProjectList.Normalize(RecentProjects);
        Outputs = (Outputs ?? []).Where(o => o is not null).ToList();
        if (SacnCid == Guid.Empty)
        {
            SacnCid = Guid.NewGuid();
        }
    }
}
