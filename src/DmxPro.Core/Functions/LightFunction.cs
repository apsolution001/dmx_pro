using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>
/// Base of every function (scene, chaser, …). Saved polymorphically with a <c>type</c> discriminator.
/// A function belongs to the project library, or has exactly one owner: a show (<see cref="OwnerShowId"/>, functions
/// placed in a show are copied so the show can change them without touching the original) or another function
/// (<see cref="OwnerFunctionId"/>, a chaser's own step functions). Never both.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SceneFunction), "Scene")]
[JsonDerivedType(typeof(ChaserFunction), "Chaser")]
[JsonDerivedType(typeof(SequenceFunction), "Sequence")]
[JsonDerivedType(typeof(ColorCycleFunction), "ColorCycle")]
[JsonDerivedType(typeof(FadeFunction), "Fade")]
[JsonDerivedType(typeof(PulseFunction), "Pulse")]
[JsonDerivedType(typeof(FlashFunction), "Flash")]
[JsonDerivedType(typeof(StrobeFunction), "Strobe")]
public abstract class LightFunction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Kind of function. Not serialized: the JSON <c>type</c> discriminator carries it. Overrides must repeat
    /// <c>[JsonIgnore]</c> (the attribute is not inherited), or the JSON gets a duplicate <c>type</c> property.
    /// </summary>
    [JsonIgnore]
    public abstract FunctionType Type { get; }

    /// <summary>The show that owns this function as its own copy; null for a normal library function.</summary>
    public Guid? OwnerShowId { get; set; }

    /// <summary>
    /// The function (a chaser) that owns this one as a step: an own-look scene or a copy made for the step. Such a
    /// function is always hidden from the Functions list, Virtual Console targets, the Shows library and pickers, and
    /// lives and dies with its owner. Null when not owned by a function. Never set together with <see cref="OwnerShowId"/>.
    /// </summary>
    public Guid? OwnerFunctionId { get; set; }

    /// <summary>True when another function (a chaser) owns this one (<see cref="OwnerFunctionId"/> is set).</summary>
    [JsonIgnore]
    public bool IsOwnedByFunction => OwnerFunctionId is not null;

    /// <summary>
    /// True when the Functions list does not show this function: it is owned by a function, or it is a show copy without
    /// "Show in function list".
    /// </summary>
    [JsonIgnore]
    public bool IsHiddenFromList => OwnerFunctionId is not null || (OwnerShowId is not null && !ShowInFunctionList);

    /// <summary>
    /// Whether a show-owned copy also appears in the Functions list. Library functions are always listed, whatever
    /// this flag says.
    /// </summary>
    public bool ShowInFunctionList { get; set; } = true;

    /// <summary>The function this one was copied from. Informational only; the copy never follows the source.</summary>
    public Guid? CopiedFromId { get; set; }

    /// <summary>
    /// Deep copy with a new <see cref="Id"/> and <see cref="CopiedFromId"/> set to this function's id. Ownership and
    /// list flags are copied as-is; callers adjust them.
    /// </summary>
    public LightFunction CloneAsNew()
    {
        var copy = CloneCore();
        copy.Id = Guid.NewGuid();
        copy.Name = Name;
        copy.OwnerShowId = OwnerShowId;
        copy.OwnerFunctionId = OwnerFunctionId;
        copy.ShowInFunctionList = ShowInFunctionList;
        copy.CopiedFromId = Id;
        return copy;
    }

    /// <summary>Deep copy of the type-specific data; base properties are set by <see cref="CloneAsNew"/>.</summary>
    protected abstract LightFunction CloneCore();
}
