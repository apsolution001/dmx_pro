namespace DmxPro.App.Services;

/// <summary>Switches between the dark and light Fluent theme.</summary>
public interface IThemeService
{
    bool IsDark { get; }

    void SetDark(bool dark);
}
