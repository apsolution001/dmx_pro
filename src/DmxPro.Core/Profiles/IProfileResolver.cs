namespace DmxPro.Core.Profiles;

/// <summary>Looks up fixture profiles by id.</summary>
public interface IProfileResolver
{
    FixtureProfile? FindProfile(string profileId);
}
