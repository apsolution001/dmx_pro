using DmxPro.Core.Functions;
using DmxPro.Core.Validation;
using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Projects;

/// <summary>
/// Virtual Console layout: pages of widgets that reference functions, shows and groups by id. Only the layout is saved;
/// operator lock, pressed/running state and fader levels are session state and never part of the project.
/// </summary>
public sealed partial class Project
{
    /// <summary>Colour presets for the suggested first page, in spec order (§6): name of the function to look for + colour.</summary>
    private static readonly (string Name, string Color)[] DefaultPageColors =
    [
        ("Red", "#FF0000"),
        ("Green", "#00FF00"),
        ("Blue", "#0000FF"),
        ("White", "#FFFFFF"),
        ("Cyan", "#00FFFF"),
        ("Magenta", "#FF00FF"),
        ("Yellow", "#FFFF00"),
    ];

    /// <summary>Console pages in tab order. Projects saved before the console existed load with an empty list.</summary>
    public List<ConsolePage> ConsolePages { get; set; } = [];

    public ConsolePage? FindConsolePage(Guid pageId) => ConsolePages.FirstOrDefault(p => p.Id == pageId);

    public ConsolePage AddConsolePage(string name)
    {
        var page = new ConsolePage { Name = name };
        ConsolePages.Add(page);
        return page;
    }

    public void RenameConsolePage(Guid pageId, string name) => GetConsolePage(pageId).Name = name;

    public bool RemoveConsolePage(Guid pageId) => ConsolePages.RemoveAll(p => p.Id == pageId) > 0;

    /// <summary>
    /// Adds <paramref name="widget"/> to a page. Its size and position are clamped so it lies fully on the page; it may
    /// overlap other widgets (use <see cref="ConsolePage.FindFreeSlot"/> to pick an empty spot first).
    /// </summary>
    public ConsoleWidget AddConsoleWidget(Guid pageId, ConsoleWidget widget)
    {
        var page = GetConsolePage(pageId);
        if (page.FindWidget(widget.Id) is not null)
        {
            throw new ArgumentException("The widget is already on this page.", nameof(widget));
        }

        page.FitInside(widget);
        page.Widgets.Add(widget);
        return widget;
    }

    /// <summary>Moves a widget, snapping to the nearest grid cell and keeping it fully on the page.</summary>
    public void MoveConsoleWidget(Guid pageId, Guid widgetId, double x, double y)
    {
        var (page, widget) = GetConsoleWidget(pageId, widgetId);
        page.SetPosition(widget, x, y);
    }

    /// <summary>Resizes a widget, snapping to whole cells, at least 1×1, and never past the page edge.</summary>
    public void ResizeConsoleWidget(Guid pageId, Guid widgetId, double width, double height)
    {
        var (page, widget) = GetConsoleWidget(pageId, widgetId);
        page.SetSize(widget, width, height);
    }

    /// <summary>
    /// Copies a widget (new id, same target, label, colour, size and mode) into the first free slot of the page, or on
    /// top of the original when the page is full. The key shortcut is not copied, so one key never triggers two widgets.
    /// </summary>
    public ConsoleWidget DuplicateConsoleWidget(Guid pageId, Guid widgetId)
    {
        var (page, source) = GetConsoleWidget(pageId, widgetId);
        var copy = source.CloneAsNew();
        copy.KeyShortcut = null;
        if (page.FindFreeSlot(copy.Width, copy.Height) is { } slot)
        {
            copy.X = slot.X;
            copy.Y = slot.Y;
        }

        page.Widgets.Insert(page.Widgets.IndexOf(source) + 1, copy);
        return copy;
    }

    public bool RemoveConsoleWidget(Guid pageId, Guid widgetId) =>
        GetConsolePage(pageId).Widgets.RemoveAll(w => w.Id == widgetId) > 0;

    /// <summary>
    /// Creates the suggested first page (spec §6): Blackout, Release All and Master on top; below them a button for each
    /// of Red, Green, Blue, White, Cyan, Magenta, Yellow that exists as a library function (matched by name); below that
    /// a toggle for each library chaser. Missing colours/chasers are simply left out.
    /// </summary>
    public ConsolePage CreateDefaultConsolePage(string name = "Main")
    {
        var page = AddConsolePage(name);
        page.Widgets.Add(new BlackoutWidget { Label = "Blackout", X = 0, Y = 0 });
        page.Widgets.Add(new ReleaseAllWidget { Label = "Release All", X = 2, Y = 0 });
        page.Widgets.Add(new MasterWidget { Label = "Master", X = 4, Y = 0, Width = 4, Height = 2 });

        var library = Functions.Where(f => f.OwnerShowId is null).ToList();
        var x = 0;
        foreach (var (colorName, color) in DefaultPageColors)
        {
            var function = library.FirstOrDefault(
                f => string.Equals(f.Name.Trim(), colorName, StringComparison.OrdinalIgnoreCase));
            if (function is not null)
            {
                page.Widgets.Add(new FunctionWidget { FunctionId = function.Id, Label = colorName, Color = color, X = x, Y = 2 });
                x += ConsoleWidget.DefaultWidth;
            }
        }

        x = 0;
        foreach (var chaser in library.Where(f => f.Type == FunctionType.Chaser))
        {
            if (x + ConsoleWidget.DefaultWidth > page.Columns)
            {
                break;
            }

            page.Widgets.Add(new FunctionWidget { FunctionId = chaser.Id, Label = chaser.Name, X = x, Y = 4 });
            x += ConsoleWidget.DefaultWidth;
        }

        return page;
    }

    /// <summary>
    /// Console checks: widgets without a target or pointing at a missing function/show/group, widgets outside their
    /// page, and key shortcuts used twice on one page. All are warnings.
    /// </summary>
    public IReadOnlyList<ValidationIssue> ValidateConsole()
    {
        var issues = new List<ValidationIssue>();
        var functionIds = Functions.Select(f => f.Id).ToHashSet();
        var showIds = Shows.Select(s => s.Id).ToHashSet();
        var groupIds = Groups.Select(g => g.Id).ToHashSet();

        foreach (var page in ConsolePages)
        {
            foreach (var widget in page.Widgets)
            {
                var (kind, targetId, required) = widget switch
                {
                    FunctionWidget f => ("function", (Guid?)f.FunctionId, true),
                    ShowWidget s => ("show", s.ShowId, true),
                    GroupLevelWidget g => ("group", g.GroupId, true),
                    SpeedWidget s => ("function", s.FunctionId, false),
                    _ => (string.Empty, null, false),
                };

                if (required && targetId == Guid.Empty)
                {
                    issues.Add(ValidationIssue.Warning(
                        "CONSOLE_WIDGET_UNASSIGNED",
                        $"Widget '{widget.Label}' on console page '{page.Name}' has no {kind} assigned.",
                        widget.Id));
                }
                else if (targetId is { } id && id != Guid.Empty)
                {
                    var exists = kind switch
                    {
                        "show" => showIds.Contains(id),
                        "group" => groupIds.Contains(id),
                        _ => functionIds.Contains(id),
                    };
                    if (!exists)
                    {
                        issues.Add(ValidationIssue.Warning(
                            "CONSOLE_WIDGET_MISSING_TARGET",
                            $"Widget '{widget.Label}' on console page '{page.Name}' controls a {kind} that no longer exists.",
                            widget.Id));
                    }
                }

                if (!page.Contains(widget))
                {
                    issues.Add(ValidationIssue.Warning(
                        "CONSOLE_WIDGET_OUT_OF_PAGE",
                        $"Widget '{widget.Label}' does not fit on console page '{page.Name}'.",
                        widget.Id));
                }
            }

            var duplicateShortcuts = page.Widgets
                .Where(w => !string.IsNullOrWhiteSpace(w.KeyShortcut))
                .GroupBy(w => w.KeyShortcut!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);
            foreach (var shortcut in duplicateShortcuts)
            {
                issues.Add(ValidationIssue.Warning(
                    "CONSOLE_DUPLICATE_SHORTCUT",
                    $"Key '{shortcut.Key}' is used by {shortcut.Count()} widgets on console page '{page.Name}'.",
                    page.Id));
            }
        }

        return issues;
    }

    private ConsolePage GetConsolePage(Guid pageId) =>
        FindConsolePage(pageId) ?? throw new ArgumentException("Console page not found.", nameof(pageId));

    private (ConsolePage Page, ConsoleWidget Widget) GetConsoleWidget(Guid pageId, Guid widgetId)
    {
        var page = GetConsolePage(pageId);
        var widget = page.FindWidget(widgetId) ?? throw new ArgumentException("Widget not found.", nameof(widgetId));
        return (page, widget);
    }
}
