using DmxPro.App.ViewModels;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>Search box, folder expansion and filter-hidden selection of the Functions page.</summary>
public sealed class FunctionsViewModelSearchTests : IDisposable
{
    private readonly AppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void Search_TwoWordsInAnyOrder_MatchesOnlyFunctionsContainingBoth()
    {
        var vm = CreateWithLibrary();

        vm.Search = "red wash";

        Assert.Equal(["Wash red front"], Names(vm));
    }

    [Fact]
    public void Search_DifferentCaseAndExtraSpaces_MatchesTheSame()
    {
        var vm = CreateWithLibrary();

        vm.Search = "   RED    Wash  ";

        Assert.Equal(["Wash red front"], Names(vm));
    }

    [Fact]
    public void Search_TypeName_MatchesFunctionsOfThatType()
    {
        var vm = CreateWithLibrary();

        // "Red chase" does not contain "chaser"; its type does.
        vm.Search = "chaser";

        Assert.Equal(["Red chase"], Names(vm));
    }

    [Fact]
    public void Search_WordsSplitAcrossNameAndType_Match()
    {
        var vm = CreateWithLibrary();

        // "blue" only in the name, "scene" only in the type.
        vm.Search = "scene blue";

        Assert.Equal(["Blue wash"], Names(vm));
    }

    [Fact]
    public void Search_MultiWordTypeName_MatchesColorCycle()
    {
        var vm = Create(project =>
        {
            project.CreateFunction(FunctionType.ColorCycle, "Rainbow");
            project.CreateFunction(FunctionType.Scene, "Colors");
        });

        vm.Search = "cycle color";

        Assert.Equal(["Rainbow"], Names(vm));
    }

    [Fact]
    public void Search_ShowName_MatchesListedShowCopies()
    {
        var vm = Create(project =>
        {
            project.CreateFunction(FunctionType.Scene, "Intro library");
            var show = project.CreateShow("Wedding");
            var item = project.CreateFunctionInShow(show.Id, show.Tracks[0].Id, FunctionType.Scene, "Intro", 0, 4);
            project.SetShowInFunctionList(item.FunctionId, true);
        });

        vm.Search = "wedding";
        var byShow = Names(vm);
        vm.Search = "intro wedding";
        var byShowAndName = Names(vm);

        Assert.Equal(["Intro"], byShow);
        Assert.Equal(["Intro"], byShowAndName);
    }

    [Fact]
    public void Search_NothingMatches_ShowsNoFilterResults()
    {
        var vm = CreateWithLibrary();

        vm.Search = "zzz";

        Assert.Empty(vm.Rows);
        Assert.Empty(vm.Groups);
        Assert.True(vm.NoFilterResults);
        Assert.False(vm.IsListEmpty);
    }

    [Fact]
    public void Search_WhitespaceOnly_ListsEverything()
    {
        var vm = CreateWithLibrary();

        vm.Search = "   ";

        Assert.Equal(3, vm.Rows.Count);
    }

    [Fact]
    public void Search_WhileFolderCollapsed_OpensEveryFolder()
    {
        var vm = CreateWithLibrary();
        Group(vm, FunctionType.Scene).IsExpanded = false;

        vm.Search = "a";

        Assert.All(vm.Groups, g => Assert.True(g.IsExpanded));
    }

    [Fact]
    public void ClearSearch_AfterSeveralKeystrokes_RestoresTheUsersCollapsedFolders()
    {
        var vm = CreateWithLibrary();
        Group(vm, FunctionType.Scene).IsExpanded = false;

        vm.Search = "r";
        vm.Search = "re";
        vm.Search = "red";
        vm.Search = string.Empty;

        Assert.False(Group(vm, FunctionType.Scene).IsExpanded);
        Assert.True(Group(vm, FunctionType.Chaser).IsExpanded);
    }

    [Fact]
    public void Search_WhitespaceOnly_KeepsCollapsedFoldersCollapsed()
    {
        var vm = CreateWithLibrary();
        Group(vm, FunctionType.Scene).IsExpanded = false;

        vm.Search = "  ";

        Assert.False(Group(vm, FunctionType.Scene).IsExpanded);
    }

    [Fact]
    public void Search_HidesSelectedFunction_SelectionComesBackWhenCleared()
    {
        var vm = CreateWithLibrary();
        vm.SelectedRow = Row(vm, "Blue wash");

        vm.Search = "red";
        var whileHidden = vm.SelectedRow;
        vm.Search = string.Empty;

        Assert.Null(whileHidden);
        Assert.Equal("Blue wash", vm.SelectedRow?.Name);
        Assert.True(vm.SelectedRow!.IsSelected);
    }

    [Fact]
    public void Search_SelectionHiddenAcrossKeystrokes_ComesBackWhenCleared()
    {
        var vm = CreateWithLibrary();
        vm.SelectedRow = Row(vm, "Blue wash");

        vm.Search = "r";
        vm.Search = "re";
        vm.Search = "red";
        vm.Search = string.Empty;

        Assert.Equal("Blue wash", vm.SelectedRow?.Name);
    }

    [Fact]
    public void Search_MatchesSelectedFunction_KeepsItSelected()
    {
        var vm = CreateWithLibrary();
        vm.SelectedRow = Row(vm, "Blue wash");

        vm.Search = "blue";

        Assert.Equal("Blue wash", vm.SelectedRow?.Name);
    }

    [Fact]
    public void NewFunctionTypes_DoNotContainSequence()
    {
        var vm = CreateWithLibrary();

        Assert.DoesNotContain(vm.TypeOptions, o => o.Type == FunctionType.Sequence);
        Assert.Contains(vm.TypeOptions, o => o.Type == FunctionType.Chaser);
    }

    [Fact]
    public void TypeFilter_HidesSelectedFunction_SelectionComesBackWithAllTypes()
    {
        var vm = CreateWithLibrary();
        vm.SelectedRow = Row(vm, "Blue wash");
        var allTypes = vm.TypeFilter;

        vm.TypeFilter = vm.TypeOptions.First(o => o.Type == FunctionType.Chaser);
        var whileHidden = vm.SelectedRow;
        vm.TypeFilter = allTypes;

        Assert.Null(whileHidden);
        Assert.Equal("Blue wash", vm.SelectedRow?.Name);
    }

    [Fact]
    public void Search_UserSelectsAnotherFunctionWhileFiltered_HiddenSelectionIsForgotten()
    {
        var vm = CreateWithLibrary();
        vm.SelectedRow = Row(vm, "Blue wash");

        vm.Search = "red";
        vm.SelectedRow = Row(vm, "Red chase");
        vm.Search = string.Empty;

        Assert.Equal("Red chase", vm.SelectedRow?.Name);
    }

    [Fact]
    public void Search_UserSelectsFolderWhileFiltered_HiddenSelectionIsForgotten()
    {
        var vm = CreateWithLibrary();
        vm.SelectedRow = Row(vm, "Blue wash");

        vm.Search = "red";
        Group(vm, FunctionType.Chaser).IsSelected = true;
        vm.Search = string.Empty;

        // Selecting a folder means "no function selected" (FunctionsViewModel.OnSelectedRowChanged), so the
        // selection the search was hiding must not come back.
        Assert.Null(vm.SelectedRow);
    }

    [Fact]
    public void Search_SelectedFunctionDeletedWhileHidden_NothingSelectedAfterClear()
    {
        var vm = CreateWithLibrary();
        var blue = Row(vm, "Blue wash").Function;
        vm.SelectedRow = Row(vm, "Blue wash");

        vm.Search = "red";
        _rig.Projects.Current.DeleteFunction(blue.Id);
        _rig.Projects.MarkChanged();
        vm.Search = string.Empty;

        Assert.Null(vm.SelectedRow);
    }

    [Fact]
    public void ClearSearchCommand_EmptySearch_CannotExecute()
    {
        var vm = CreateWithLibrary();

        Assert.False(vm.ClearSearchCommand.CanExecute(null));
    }

    [Fact]
    public void ClearSearchCommand_SearchTyped_CanExecuteAndRaisesChanged()
    {
        var vm = CreateWithLibrary();
        var raised = 0;
        vm.ClearSearchCommand.CanExecuteChanged += (_, _) => raised++;

        vm.Search = "red";

        Assert.True(vm.ClearSearchCommand.CanExecute(null));
        Assert.True(raised > 0);
    }

    [Fact]
    public void ClearSearchCommand_Execute_EmptiesSearchAndListsEverything()
    {
        var vm = CreateWithLibrary();
        vm.Search = "red wash";

        vm.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, vm.Search);
        Assert.Equal(3, vm.Rows.Count);
        Assert.False(vm.ClearSearchCommand.CanExecute(null));
    }

    [Fact]
    public void ClearSearchCommand_WhitespaceOnlySearch_CanExecute()
    {
        var vm = CreateWithLibrary();

        vm.Search = " ";

        Assert.True(vm.ClearSearchCommand.CanExecute(null));
    }

    private static List<string> Names(FunctionsViewModel vm) => vm.Rows.Select(r => r.Name).ToList();

    private static FunctionTreeGroupViewModel Group(FunctionsViewModel vm, FunctionType type) => vm.Groups.Single(g => g.Type == type);

    private static FunctionRowViewModel Row(FunctionsViewModel vm, string name) => vm.Rows.Single(r => r.Name == name);

    private FunctionsViewModel CreateWithLibrary() => Create(project =>
    {
        project.CreateFunction(FunctionType.Scene, "Wash red front");
        project.CreateFunction(FunctionType.Scene, "Blue wash");
        project.CreateFunction(FunctionType.Chaser, "Red chase");
    });

    /// <summary>Fills the open project first, so the view model lists it from its first rebuild.</summary>
    private FunctionsViewModel Create(Action<Project> build)
    {
        build(_rig.Projects.Current);
        return _rig.CreateFunctionsViewModel();
    }
}
