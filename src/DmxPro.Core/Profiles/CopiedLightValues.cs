namespace DmxPro.Core.Profiles;

/// <summary>
/// The channel values of one light, copied so they can be pasted onto other lights (QLC+ "copy current values").
/// Pasting is profile-driven, never by channel position: onto a light of the same profile every channel goes to the
/// channel with the same key; onto a light of another profile each channel goes to the channel of the same
/// <see cref="ChannelType"/> and the same occurrence in the mode (the first Red to the first Red, the second White to
/// the second White). A <see cref="ChannelType.Fine"/> channel goes to the fine channel whose coarse channel has the same
/// type and occurrence (Red fine to Red fine, never to Dimmer fine). Channels of unknown meaning
/// (<see cref="ChannelType.Generic"/>, and fine channels of a Generic or missing coarse channel) are only pasted onto the
/// same profile, and target channels without a match are left unchanged.
/// </summary>
public sealed class CopiedLightValues
{
    private CopiedLightValues(string profileId, string sourceName, IReadOnlyList<CopiedChannelValue> channels)
    {
        ProfileId = profileId;
        SourceName = sourceName;
        Channels = channels;
    }

    /// <summary>Profile id of the light the values were copied from.</summary>
    public string ProfileId { get; }

    /// <summary>Name of the light the values were copied from (for messages).</summary>
    public string SourceName { get; }

    /// <summary>The copied channels, in the source mode's channel order.</summary>
    public IReadOnlyList<CopiedChannelValue> Channels { get; }

    /// <summary>
    /// Copies every channel of <paramref name="channels"/>: pass the light's whole mode channel list in order (an
    /// occurrence such as "the second White" is counted over the channels passed). To copy only some channels, use
    /// <see cref="CreateFromMode"/> so occurrences are still counted over the whole mode.
    /// </summary>
    public static CopiedLightValues Create(string profileId, string sourceName, IEnumerable<(ChannelDefinition Channel, byte Value)> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var all = new List<(ChannelDefinition, byte, bool)>();
        foreach (var (channel, value) in channels)
        {
            all.Add((channel, value, true));
        }

        return CreateFromMode(profileId, sourceName, all);
    }

    /// <summary>
    /// Copies the channels flagged <c>Include</c> of a light. <paramref name="modeChannels"/> is the light's whole
    /// mode channel list in order (included or not), so occurrences and fine-to-coarse links are resolved against the
    /// real mode: a scene that includes only the second White copies it as the second White, not the first.
    /// </summary>
    public static CopiedLightValues CreateFromMode(
        string profileId,
        string sourceName,
        IEnumerable<(ChannelDefinition Channel, byte Value, bool Include)> modeChannels)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(modeChannels);
        var entries = modeChannels as IReadOnlyList<(ChannelDefinition Channel, byte Value, bool Include)> ?? modeChannels.ToList();
        var definitions = new ChannelDefinition[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            definitions[i] = entries[i].Channel;
        }

        var signatures = Describe(definitions);
        var list = new List<CopiedChannelValue>();
        for (var i = 0; i < entries.Count; i++)
        {
            var (channel, value, include) = entries[i];
            if (include)
            {
                var signature = signatures[i];
                list.Add(new CopiedChannelValue(channel.Key, channel.Type, signature.Occurrence, value, signature.CoarseType));
            }
        }

        return new CopiedLightValues(profileId, sourceName ?? string.Empty, list);
    }

    /// <summary>
    /// Which value goes to which channel of a target light. <paramref name="targetChannels"/> is the target mode's
    /// channel list in order (all of them, so occurrences are counted the same way as the light's own channels).
    /// Returns one entry per target channel that receives a value, as (index in <paramref name="targetChannels"/>, value).
    /// </summary>
    public IReadOnlyList<(int Index, byte Value)> MatchTo(string targetProfileId, IReadOnlyList<ChannelDefinition> targetChannels)
    {
        ArgumentNullException.ThrowIfNull(targetChannels);
        var result = new List<(int, byte)>();
        if (string.Equals(targetProfileId, ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            var byKey = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (var copied in Channels)
            {
                byKey[copied.Key] = copied.Value;
            }

            for (var i = 0; i < targetChannels.Count; i++)
            {
                if (byKey.TryGetValue(targetChannels[i].Key, out var value))
                {
                    result.Add((i, value));
                }
            }

            return result;
        }

        var bySignature = new Dictionary<Signature, byte>();
        foreach (var copied in Channels)
        {
            var signature = new Signature(copied.Type, copied.Occurrence, copied.CoarseType);
            if (signature.IsPortable)
            {
                bySignature[signature] = copied.Value;
            }
        }

        var targetSignatures = Describe(targetChannels);
        for (var i = 0; i < targetSignatures.Length; i++)
        {
            var signature = targetSignatures[i];
            if (signature.IsPortable && bySignature.TryGetValue(signature, out var value))
            {
                result.Add((i, value));
            }
        }

        return result;
    }

    /// <summary>
    /// The profile-independent identity of every channel of a mode: type + occurrence of that type, and for a fine
    /// channel the type + occurrence of its coarse channel (resolved by <see cref="ChannelDefinition.CoarseKey"/> within
    /// the same mode).
    /// </summary>
    private static Signature[] Describe(IReadOnlyList<ChannelDefinition> channels)
    {
        var signatures = new Signature[channels.Count];
        var occurrences = new Dictionary<ChannelType, int>();
        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            if (channel.Key is { } key)
            {
                indexByKey.TryAdd(key, i);
            }

            if (channel.Type == ChannelType.Fine)
            {
                continue;
            }

            var occurrence = occurrences.GetValueOrDefault(channel.Type);
            occurrences[channel.Type] = occurrence + 1;
            signatures[i] = new Signature(channel.Type, occurrence, null);
        }

        for (var i = 0; i < channels.Count; i++)
        {
            if (channels[i].Type != ChannelType.Fine)
            {
                continue;
            }

            signatures[i] = channels[i].CoarseKey is { } coarseKey
                && indexByKey.TryGetValue(coarseKey, out var coarseIndex)
                && channels[coarseIndex].Type != ChannelType.Fine
                    ? new Signature(ChannelType.Fine, signatures[coarseIndex].Occurrence, signatures[coarseIndex].Type)
                    : new Signature(ChannelType.Fine, -1, null);
        }

        return signatures;
    }

    private readonly record struct Signature(ChannelType Type, int Occurrence, ChannelType? CoarseType)
    {
        /// <summary>
        /// True when the meaning is known well enough to paste onto another profile: not Generic, and a fine channel has
        /// a known, non-Generic coarse channel.
        /// </summary>
        public bool IsPortable => Type switch
        {
            ChannelType.Generic => false,
            ChannelType.Fine => CoarseType is { } coarse && coarse != ChannelType.Generic,
            _ => true,
        };
    }
}
