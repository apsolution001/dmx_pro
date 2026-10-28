using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>Chaser-owned step functions never show up in the Functions, Shows or Virtual Console lists.</summary>
public sealed class OwnedFunctionVisibilityTests : IDisposable
{
    private readonly AppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void FunctionsPage_ShowOnlyCopiesOn_ChaserOwnedStillHidden()
    {
        var (_, chaser, _) = BuildChaserWithOwnedStep(_rig.Projects.Current);
        _rig.Projects.Current.CreateFunction(FunctionType.Scene, "Library scene");
        var vm = _rig.CreateFunctionsViewModel();

        vm.ShowHiddenShowCopies = true;

        Assert.DoesNotContain(vm.Rows, r => r.Function.OwnerFunctionId is not null);
        Assert.Contains(vm.Rows, r => r.Function == chaser);
        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public void VirtualConsoleTargets_ExcludeChaserOwned()
    {
        var (_, chaser, owned) = BuildChaserWithOwnedStep(_rig.Projects.Current);
        var console = new VirtualConsoleViewModel(_rig.Projects, _rig.Playback, _rig.Dialogs, _rig.Settings, _rig.Music);

        Assert.Contains(console.FunctionOptions, o => o.Id == chaser.Id);
        Assert.DoesNotContain(console.FunctionOptions, o => o.Id == owned.Id);
        Assert.DoesNotContain(console.SpeedOptions, o => o.Id == owned.Id);
    }

    [Fact]
    public void ShowsLibrary_ExcludeChaserOwned()
    {
        var (_, chaser, owned) = BuildChaserWithOwnedStep(_rig.Projects.Current);
        var shows = new ShowsViewModel(
            _rig.Projects, _rig.Dialogs, _rig.Settings, _rig.Clipboard, _rig.Music, _rig.Playback, new NoFileDialogs(), _rig.Preview, _rig.LiveMode);

        Assert.Contains(shows.Library, l => l.Id == chaser.Id);
        Assert.DoesNotContain(shows.Library, l => l.Id == owned.Id);
    }

    [Fact]
    public void Functions_CopyPasteChaser_DeepCopiesSteps()
    {
        var project = _rig.Projects.Current;
        var (_, chaser, owned) = BuildChaserWithOwnedStep(project);
        var vm = _rig.CreateFunctionsViewModel();
        vm.SelectedRow = vm.Rows.First(r => r.Function == chaser);

        vm.CopyCommand.Execute(null);
        vm.PasteCommand.Execute(null);

        var chasers = project.Functions.OfType<ChaserFunction>().ToList();
        Assert.Equal(2, chasers.Count);
        var copy = chasers.Single(c => c != chaser);
        var copiedStep = project.FindFunction(copy.Steps[0].FunctionId);
        Assert.NotNull(copiedStep);
        Assert.NotEqual(owned.Id, copiedStep.Id);
        Assert.Equal(copy.Id, copiedStep.OwnerFunctionId);
        Assert.Equal(chaser.Id, owned.OwnerFunctionId);
        Assert.Equal(chaser.Steps[0].FunctionId, owned.Id);
    }

    private static (Project Project, ChaserFunction Chaser, SceneFunction Owned) BuildChaserWithOwnedStep(Project project)
    {
        var chaser = (ChaserFunction)project.CreateFunction(FunctionType.Chaser, "Chase");
        var owned = (SceneFunction)project.CreateFunction(FunctionType.Scene, "Step 1 look");
        owned.OwnerFunctionId = chaser.Id;
        owned.ShowInFunctionList = false;
        chaser.Steps.Add(new ChaserStep { FunctionId = owned.Id });
        return (project, chaser, owned);
    }

    private sealed class NoFileDialogs : IFileDialogService
    {
        public string? ShowOpenProject(string initialDirectory) => null;

        public string? ShowSaveProject(string initialDirectory, string suggestedName) => null;

        public IReadOnlyList<string> ShowOpenProfiles() => [];

        public string? ShowOpenAudio(string? initialDirectory) => null;
    }
}
