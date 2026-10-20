namespace DmxPro.Engine.Testing;

/// <summary>One fixture channel under test in a <see cref="ChannelTestSession"/>.</summary>
/// <param name="FixtureId">Fixture id.</param>
/// <param name="ChannelKey">Profile channel key.</param>
/// <param name="Universe">Project universe (from 1) the channel resolved to.</param>
/// <param name="Address">DMX address 1–512 the channel resolved to.</param>
/// <param name="Value">Requested test value.</param>
/// <param name="Sequence">Order of the last touch (higher = touched later); the latest wins a shared address.</param>
/// <param name="Generation">
/// <see cref="DmxEngine.ReleaseGeneration"/> when the value was last sent; lower than the engine's current generation
/// means an engine-wide clear removed it.
/// </param>
public readonly record struct TestOverride(
    Guid FixtureId,
    string ChannelKey,
    int Universe,
    int Address,
    byte Value,
    long Sequence,
    long Generation);
