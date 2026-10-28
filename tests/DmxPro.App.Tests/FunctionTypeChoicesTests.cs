using DmxPro.App.Services;
using DmxPro.App.ViewModels;
using DmxPro.App.ViewModels.Editors;
using DmxPro.Core.Functions;
using DmxPro.Core.Projects;

namespace DmxPro.App.Tests;

/// <summary>
/// Owner decision 2026-10-28: the app creates and edits scenes and chasers only. The old effect types stay loadable in
/// Core but are never offered, and a leftover one opens a short note instead of crashing.
/// </summary>
public sealed class FunctionTypeChoicesTests : IDisposable
{
    private readonly AppRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void CreatableTypes_AreExactlySceneAndChaser()
    {
        Assert.Equal([FunctionType.Scene, FunctionType.Chaser], FunctionTypeOption.CreatableTypes);
    }

    [Fact]
    public void FunctionsPage_TypeFilter_IsAllScenesAndChasers()
    {
        var vm = _rig.CreateFunctionsViewModel();

        Assert.Equal([null, FunctionType.Scene, FunctionType.Chaser], vm.TypeOptions.Select(o => o.Type));
    }

    [Fact]
    public void ShowsPage_NewFunctionTypes_AreExactlySceneAndChaser()
    {
        var shows = new ShowsViewModel(
            _rig.Projects, _rig.Dialogs, _rig.Settings, _rig.Clipboard, _rig.Music, _rig.Playback, new NoFileDialogs(), _rig.Preview, _rig.LiveMode);

        Assert.Equal([FunctionType.Scene, FunctionType.Chaser], shows.NewFunctionTypes.Select(o => o.Type));
    }

    [Fact]
    public async Task NewFunction_LegacyType_CreatesNothing()
    {
        var vm = _rig.CreateFunctionsViewModel();

        await vm.NewFunctionCommand.ExecuteAsync(FunctionType.Pulse);

        Assert.Empty(_rig.Projects.Current.Functions);
        Assert.Null(vm.Editor);
    }

    [Fact]
    public async Task NewFunction_Chaser_CreatesChaserAndOpensItsEditor()
    {
        var vm = _rig.CreateFunctionsViewModel();

        await vm.NewFunctionCommand.ExecuteAsync(FunctionType.Chaser);

        Assert.IsType<ChaserFunction>(Assert.Single(_rig.Projects.Current.Functions));
        Assert.IsType<ChaserEditorViewModel>(vm.Editor);
    }

    [Fact]
    public void Factory_LegacyFunction_OpensUnsupportedNote()
    {
        var project = new Project();
        var pulse = new PulseFunction { Name = "Old pulse" };
        project.Functions.Add(pulse);

        var editor = FunctionEditorFactory.Create(project, pulse, _rig.Settings, _rig.Clipboard);

        var note = Assert.IsType<UnsupportedFunctionEditorViewModel>(editor);
        Assert.Contains("no longer supported", note.Message, StringComparison.Ordinal);
        Assert.False(note.IsDirty);
    }

    [Fact]
    public void ChaserPicker_OffersOnlyScenesAndChasers()
    {
        var project = new Project();
        var chaser = new ChaserFunction { Name = "Party" };
        var scene = new SceneFunction { Name = "Red" };
        var other = new ChaserFunction { Name = "Other" };
        project.Functions.Add(chaser);
        project.Functions.Add(scene);
        project.Functions.Add(other);
        project.Functions.Add(new PulseFunction { Name = "Throb" });
        project.Functions.Add(new StrobeFunction { Name = "Zap" });

        var vm = new ChaserEditorViewModel(project, chaser, _rig.Settings, _rig.Clipboard);

        Assert.Equal(["Other", "Red"], vm.PickerItems.Select(p => p.Function.Name).Order(StringComparer.Ordinal));
    }

    private sealed class NoFileDialogs : IFileDialogService
    {
        public string? ShowOpenProject(string initialDirectory) => null;

        public string? ShowSaveProject(string initialDirectory, string suggestedName) => null;

        public IReadOnlyList<string> ShowOpenProfiles() => [];

        public string? ShowOpenAudio(string? initialDirectory) => null;
    }
}
