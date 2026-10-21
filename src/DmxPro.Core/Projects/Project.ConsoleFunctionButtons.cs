using DmxPro.Core.VirtualConsole;

namespace DmxPro.Core.Projects;

/// <summary>Functions page "Add to Virtual Console": a toggle button for one function on a console page.</summary>
public sealed partial class Project
{
    /// <summary>
    /// Adds a default-size toggle button for <paramref name="functionId"/> (labelled with the function's name) at the
    /// first free spot of the page, row by row. Returns null, and changes nothing, when no free spot is big enough.
    /// </summary>
    /// <exception cref="ArgumentException">The page or the function does not exist.</exception>
    public FunctionWidget? AddFunctionButton(Guid pageId, Guid functionId)
    {
        var page = GetConsolePage(pageId);
        var function = FindFunction(functionId)
            ?? throw new ArgumentException("Function not found.", nameof(functionId));

        if (page.FindFreeSlot(ConsoleWidget.DefaultWidth, ConsoleWidget.DefaultHeight) is not { } slot)
        {
            return null;
        }

        var widget = new FunctionWidget
        {
            FunctionId = function.Id,
            Label = function.Name,
            Mode = FunctionWidgetMode.Toggle,
            X = slot.X,
            Y = slot.Y,
        };
        AddConsoleWidget(pageId, widget);
        return widget;
    }
}
