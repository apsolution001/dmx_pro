namespace DmxPro.Core.Profiles;

/// <summary>
/// One copied channel: its profile key, type, which occurrence of that type it is in the source mode (0-based) and its
/// value. For a <see cref="ChannelType.Fine"/> channel, <see cref="CoarseType"/> is the type of its coarse channel and
/// <see cref="Occurrence"/> is that coarse channel's occurrence, so "Red fine" pastes onto the fine byte of the target's
/// matching Red. <see cref="CoarseType"/> is null for coarse channels; a fine channel whose coarse channel is not in the
/// source mode has no <see cref="CoarseType"/> and <see cref="Occurrence"/> -1, and is only pasted onto the same profile.
/// </summary>
public sealed record CopiedChannelValue(string Key, ChannelType Type, int Occurrence, byte Value, ChannelType? CoarseType = null);
