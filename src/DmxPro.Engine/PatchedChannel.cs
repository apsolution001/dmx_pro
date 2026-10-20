using DmxPro.Core.Profiles;

namespace DmxPro.Engine;

/// <summary>Where a fixture channel (resolved via profile + mode) sits in DMX, and what it means.</summary>
/// <param name="Universe">Project universe, starting at 1.</param>
/// <param name="Address">DMX address, 1–512.</param>
/// <param name="Type">Channel meaning from the profile.</param>
/// <param name="Default">Profile default, used when nothing drives the channel.</param>
public readonly record struct PatchedChannel(int Universe, int Address, ChannelType Type, byte Default);
