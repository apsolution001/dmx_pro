using System.Text.Json.Serialization;
using DmxPro.Core.Functions;
using DmxPro.Core.Shows;
using DmxPro.Core.Validation;

namespace DmxPro.Core.Projects;

/// <summary>
/// Functions and shows. A function placed in (or created in) a show is that show's own copy
/// (<see cref="LightFunction.OwnerShowId"/>), so editing it never changes the original. Show copies are hidden from the
/// Functions list unless <see cref="LightFunction.ShowInFunctionList"/> is ticked.
/// </summary>
public sealed partial class Project
{
    /// <summary>Library functions and show-owned copies (all in one list; ownership is a property).</summary>
    public List<LightFunction> Functions { get; set; } = [];

    public List<Show> Shows { get; set; } = [];

    /// <summary>
    /// What the Functions page lists: every library function plus show copies marked "Show in function list". Functions
    /// owned by another function (a chaser's steps) are never listed.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<LightFunction> FunctionListItems => Functions.Where(f => !f.IsHiddenFromList);

    public LightFunction? FindFunction(Guid id) => Functions.FirstOrDefault(f => f.Id == id);

    public Show? FindShow(Guid id) => Shows.FirstOrDefault(s => s.Id == id);

    /// <summary>Creates a show with default length/BPM and one empty track.</summary>
    public Show CreateShow(string name)
    {
        var show = new Show { Name = name };
        show.Tracks.Add(new ShowTrack { Name = "Track 1" });
        Shows.Add(show);
        return show;
    }

    public ShowTrack AddTrack(Guid showId, string name)
    {
        var track = new ShowTrack { Name = name };
        GetShow(showId).Tracks.Add(track);
        return track;
    }

    /// <summary>
    /// Inserts an empty track at <paramref name="index"/> (0 = top, the track count = after the last one). The other
    /// tracks keep their items and their order. A blank <paramref name="name"/> gets the first free "Track N" of the show.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is below 0 or above the track count.</exception>
    public ShowTrack InsertTrack(Guid showId, int index, string? name = null)
    {
        var show = GetShow(showId);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, show.Tracks.Count);

        var track = new ShowTrack { Name = string.IsNullOrWhiteSpace(name) ? NextTrackName(show) : name.Trim() };
        show.Tracks.Insert(index, track);
        return track;
    }

    /// <summary>First free "Track N" (N from 1) among the show's track names, case-insensitive.</summary>
    public static string NextTrackName(Show show)
    {
        ArgumentNullException.ThrowIfNull(show);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in show.Tracks)
        {
            names.Add(track.Name);
        }

        for (var n = 1; ; n++)
        {
            var candidate = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Track {n}");
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Removes a track and all its items (cleaning up their show-owned functions like <see cref="RemoveShowItem"/>).</summary>
    public bool RemoveTrack(Guid showId, Guid trackId)
    {
        var show = GetShow(showId);
        if (show.FindTrack(trackId) is not { } track)
        {
            return false;
        }

        show.Tracks.Remove(track);
        foreach (var item in track.Items)
        {
            RemoveFunctionIfUnused(show.Id, item.FunctionId);
        }

        return true;
    }

    /// <summary>
    /// Places <paramref name="sourceFunctionId"/> in a show. The item references a NEW show-owned copy (hidden from the
    /// Functions list, <see cref="LightFunction.CopiedFromId"/> = source), so the show can change it freely. A chaser's
    /// own step functions are copied too, owned by the new chaser copy.
    /// </summary>
    public ShowItem PlaceFunctionInShow(Guid showId, Guid trackId, Guid sourceFunctionId, double startSeconds, double lengthSeconds)
    {
        var track = GetTrack(showId, trackId);
        var source = FindFunction(sourceFunctionId)
            ?? throw new ArgumentException("Function not found.", nameof(sourceFunctionId));
        var item = NewItem(startSeconds, lengthSeconds);

        var copy = AddShowCopy(showId, source);
        item.FunctionId = copy.Id;
        track.Items.Add(item);
        return item;
    }

    /// <summary>
    /// Creates a new show-owned function of any <see cref="FunctionType"/> (default settings, see
    /// <see cref="FunctionFactory.Create"/>) directly on a track. It is hidden from the Functions list.
    /// </summary>
    public ShowItem CreateFunctionInShow(Guid showId, Guid trackId, FunctionType type, string name, double startSeconds, double lengthSeconds)
    {
        var track = GetTrack(showId, trackId);
        var item = NewItem(startSeconds, lengthSeconds);
        var function = FunctionFactory.Create(type);
        function.Name = name;
        function.OwnerShowId = showId;
        function.ShowInFunctionList = false;
        Functions.Add(function);
        item.FunctionId = function.Id;
        track.Items.Add(item);
        return item;
    }

    /// <summary>Shows/hides a show copy in the Functions list. Library functions are always listed regardless.</summary>
    public void SetShowInFunctionList(Guid functionId, bool show)
    {
        var function = FindFunction(functionId) ?? throw new ArgumentException("Function not found.", nameof(functionId));
        function.ShowInFunctionList = show;
    }

    /// <summary>
    /// Duplicates an item right after the original on the same track, together with its function: the duplicate gets
    /// its own show-owned copy (hidden from the list), so the two stay independent.
    /// </summary>
    public ShowItem DuplicateShowItem(Guid showId, Guid itemId)
    {
        var show = GetShow(showId);
        var track = show.FindTrackOfItem(itemId) ?? throw new ArgumentException("Show item not found.", nameof(itemId));
        var source = track.FindItem(itemId)!;
        var function = FindFunction(source.FunctionId)
            ?? throw new InvalidOperationException("The item's function no longer exists.");

        var copy = AddShowCopy(showId, function);

        var item = new ShowItem { FunctionId = copy.Id };
        item.SetTiming(source.EndSeconds, source.LengthSeconds, source.FadeInSeconds, source.FadeOutSeconds);
        track.Items.Insert(track.Items.IndexOf(source) + 1, item);
        return item;
    }

    /// <summary>
    /// Removes an item. Its show-owned function is deleted too unless it is listed in the Functions list or still used
    /// by another item.
    /// </summary>
    public bool RemoveShowItem(Guid showId, Guid itemId)
    {
        var show = GetShow(showId);
        if (show.FindTrackOfItem(itemId) is not { } track)
        {
            return false;
        }

        var item = track.FindItem(itemId)!;
        track.Items.Remove(item);
        RemoveFunctionIfUnused(show.Id, item.FunctionId);
        return true;
    }

    /// <summary>Changes an item's track and/or timing (see <see cref="ShowItem.SetTiming"/> for the rules).</summary>
    public void MoveShowItem(Guid showId, Guid itemId, Guid targetTrackId, double startSeconds, double lengthSeconds)
    {
        var show = GetShow(showId);
        var from = show.FindTrackOfItem(itemId) ?? throw new ArgumentException("Show item not found.", nameof(itemId));
        var to = show.FindTrack(targetTrackId) ?? throw new ArgumentException("Track not found.", nameof(targetTrackId));
        var item = from.FindItem(itemId)!;
        item.SetTiming(startSeconds, lengthSeconds, item.FadeInSeconds, item.FadeOutSeconds);
        if (!ReferenceEquals(from, to))
        {
            from.Items.Remove(item);
            to.Items.Add(item);
        }
    }

    /// <summary>
    /// Deletes a show. Its unlisted functions are removed (with their own step functions); functions ticked "Show in
    /// function list" are kept and become normal library functions.
    /// </summary>
    public bool DeleteShow(Guid showId)
    {
        if (FindShow(showId) is not { } show)
        {
            return false;
        }

        Shows.Remove(show);
        var stillUsed = Shows.SelectMany(s => s.AllItems).Select(i => i.FunctionId).ToHashSet();
        var removed = Functions.Where(f => f.OwnerShowId == showId && !f.ShowInFunctionList && !stillUsed.Contains(f.Id)).ToList();
        foreach (var function in removed)
        {
            RemoveFunctionWithOwned(function);
        }

        foreach (var function in Functions.Where(f => f.OwnerShowId == showId))
        {
            function.OwnerShowId = null;
            function.ShowInFunctionList = true;
        }

        return true;
    }

    /// <summary>
    /// Reference and timing checks for functions and shows: items pointing at missing functions, invalid item timing,
    /// scene values for missing fixtures or channel keys that the fixture's profile mode does not have.
    /// </summary>
    public IReadOnlyList<ValidationIssue> ValidateFunctionsAndShows()
    {
        var issues = new List<ValidationIssue>();
        var functionIds = Functions.Select(f => f.Id).ToHashSet();

        foreach (var show in Shows)
        {
            foreach (var item in show.AllItems)
            {
                if (!functionIds.Contains(item.FunctionId))
                {
                    issues.Add(ValidationIssue.Warning(
                        "SHOW_ITEM_MISSING_FUNCTION",
                        $"Show '{show.Name}' has an item whose function no longer exists.",
                        item.Id));
                }

                if (!item.HasValidTiming)
                {
                    issues.Add(ValidationIssue.Warning(
                        "SHOW_ITEM_INVALID_TIMING",
                        $"Show '{show.Name}' has an item with an invalid start, length or fade time.",
                        item.Id));
                }
            }
        }

        foreach (var scene in Functions.OfType<SceneFunction>())
        {
            foreach (var value in scene.Values)
            {
                if (FindFixture(value.FixtureId) is not { } fixture)
                {
                    issues.Add(ValidationIssue.Warning(
                        "SCENE_MISSING_FIXTURE",
                        $"Scene '{scene.Name}' has values for a fixture that no longer exists.",
                        scene.Id));
                    continue;
                }

                // A missing profile or mode is already reported by the patch validator.
                var mode = FindProfile(fixture.ProfileId)?.FindMode(fixture.ModeName);
                if (mode is not null && !mode.Channels.Contains(value.ChannelKey, StringComparer.Ordinal))
                {
                    issues.Add(ValidationIssue.Warning(
                        "SCENE_UNKNOWN_CHANNEL",
                        $"Scene '{scene.Name}' sets channel '{value.ChannelKey}' that fixture '{fixture.Name}' (mode {mode.Name}) does not have.",
                        scene.Id));
                }
            }
        }

        issues.AddRange(this.ValidateFunctionReferences());
        return issues;
    }

    private Show GetShow(Guid showId) =>
        FindShow(showId) ?? throw new ArgumentException("Show not found.", nameof(showId));

    private ShowTrack GetTrack(Guid showId, Guid trackId) =>
        GetShow(showId).FindTrack(trackId) ?? throw new ArgumentException("Track not found.", nameof(trackId));

    private static ShowItem NewItem(double startSeconds, double lengthSeconds)
    {
        var item = new ShowItem();
        item.SetTiming(startSeconds, lengthSeconds);
        return item;
    }

    /// <summary>Deletes a function owned by <paramref name="showId"/> when it is unlisted and no item uses it any more.</summary>
    private void RemoveFunctionIfUnused(Guid showId, Guid functionId)
    {
        if (FindFunction(functionId) is not { } function
            || function.OwnerShowId != showId
            || function.ShowInFunctionList
            || Shows.Any(s => s.AllItems.Any(i => i.FunctionId == functionId)))
        {
            return;
        }

        RemoveFunctionWithOwned(function);
    }

    /// <summary>
    /// Adds a show-owned, unlisted copy of <paramref name="source"/> plus copies of its own step functions (owned by the
    /// copy, not by the show: a function has one owner).
    /// </summary>
    private LightFunction AddShowCopy(Guid showId, LightFunction source)
    {
        var copies = this.CloneWithOwned(source);
        var copy = copies[0];
        copy.OwnerShowId = showId;
        copy.OwnerFunctionId = null;
        copy.ShowInFunctionList = false;
        Functions.AddRange(copies);
        return copy;
    }

    /// <summary>Removes <paramref name="function"/> and every function it owns (recursively).</summary>
    internal void RemoveFunctionWithOwned(LightFunction function)
    {
        var owned = this.OwnedFunctions(function.Id);
        Functions.Remove(function);
        foreach (var child in owned)
        {
            Functions.Remove(child);
        }
    }
}
