namespace DmxPro.Core.Functions;

/// <summary>
/// Function types. DMX Pro offers only <see cref="Scene"/> and <see cref="Chaser"/> (owner decision 2026-10-28); the
/// other values are legacy, load-only: they name models that old project files still contain and that are converted on
/// load (<see cref="Projects.SequenceMigration"/>, <see cref="Projects.LegacyFunctionMigration"/>).
/// </summary>
public enum FunctionType
{
    Scene,
    Chaser,
    Sequence,
    ColorCycle,
    Fade,
    Pulse,
    Flash,
    Strobe,
}
