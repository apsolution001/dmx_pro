using DmxPro.Core.Shows;

namespace DmxPro.Core.Tests;

public class ShowAudioPathsTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\" : "/";

    private static readonly string ProjectFolder = P("Shows", "Wedding");

    private static readonly string ProjectFile = Path.Combine(ProjectFolder, "wedding.dmxpro");

    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    private static Func<string, bool> Files(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    private static Show ShowWith(string filePath, string? relativePath = null) =>
        new() { Audio = new ShowAudio { FilePath = filePath, RelativePath = relativePath } };

    [Fact]
    public void Resolve_AbsolutePathExists_ReturnsAbsolutePath()
    {
        var song = P("Music", "song.mp3");
        var audio = new ShowAudio { FilePath = song, RelativePath = "other.mp3" };

        Assert.Equal(song, ShowAudioPaths.Resolve(audio, ProjectFile, Files(song, Path.Combine(ProjectFolder, "other.mp3"))));
    }

    [Fact]
    public void Resolve_AbsoluteMissing_UsesRelativePathAgainstProjectFolder()
    {
        var moved = Path.Combine(ProjectFolder, "Songs", "song.mp3");
        var audio = new ShowAudio { FilePath = P("Old", "song.mp3"), RelativePath = Path.Combine("Songs", "song.mp3") };

        Assert.Equal(moved, ShowAudioPaths.Resolve(audio, ProjectFile, Files(moved)));
    }

    [Fact]
    public void Resolve_RelativePathWithParentFolder_Resolves()
    {
        var song = P("Shows", "Music", "song.mp3");
        var audio = new ShowAudio { FilePath = P("Gone", "song.mp3"), RelativePath = Path.Combine("..", "Music", "song.mp3") };

        Assert.Equal(song, ShowAudioPaths.Resolve(audio, ProjectFile, Files(song)));
    }

    [Fact]
    public void Resolve_RelativePathWithWindowsSeparators_ResolvesOnEveryOs()
    {
        var song = Path.Combine(ProjectFolder, "Songs", "song.mp3");
        var audio = new ShowAudio { FilePath = string.Empty, RelativePath = @"Songs\song.mp3" };

        Assert.Equal(song, ShowAudioPaths.Resolve(audio, ProjectFile, Files(song)));
    }

    [Fact]
    public void Resolve_AbsoluteAndRelativeMissing_FindsFileNameInProjectFolder()
    {
        var song = Path.Combine(ProjectFolder, "song.mp3");
        var audio = new ShowAudio { FilePath = P("Old", "song.mp3"), RelativePath = Path.Combine("x", "song.mp3") };

        Assert.Equal(song, ShowAudioPaths.Resolve(audio, ProjectFile, Files(song)));
    }

    [Fact]
    public void Resolve_OnlyInMusicFolder_FindsFileNameInMusicFolder()
    {
        var song = Path.Combine(ProjectFolder, ShowAudioPaths.MusicFolderName, "song.mp3");
        var audio = new ShowAudio { FilePath = P("Old", "song.mp3") };

        Assert.Equal(song, ShowAudioPaths.Resolve(audio, ProjectFile, Files(song)));
    }

    [Fact]
    public void Resolve_ProjectFolderAndMusicFolderBothHaveFile_PrefersProjectFolder()
    {
        var inFolder = Path.Combine(ProjectFolder, "song.mp3");
        var inMusic = Path.Combine(ProjectFolder, ShowAudioPaths.MusicFolderName, "song.mp3");
        var audio = new ShowAudio { FilePath = P("Old", "song.mp3") };

        Assert.Equal(inFolder, ShowAudioPaths.Resolve(audio, ProjectFile, Files(inMusic, inFolder)));
    }

    [Fact]
    public void Resolve_OnlyRelativePathKnown_UsesItsFileNameAsFallback()
    {
        var song = Path.Combine(ProjectFolder, ShowAudioPaths.MusicFolderName, "song.mp3");
        var audio = new ShowAudio { FilePath = string.Empty, RelativePath = Path.Combine("lost", "song.mp3") };

        Assert.Equal(song, ShowAudioPaths.Resolve(audio, ProjectFile, Files(song)));
    }

    [Fact]
    public void Resolve_WindowsAbsolutePathOnAnyOs_FileNameFallbackWorks()
    {
        var song = Path.Combine(ProjectFolder, "song.mp3");
        var audio = new ShowAudio { FilePath = @"Z:\Lost\song.mp3" };

        Assert.Equal(song, ShowAudioPaths.Resolve(audio, ProjectFile, Files(song)));
    }

    [Fact]
    public void Resolve_NothingFound_ReturnsNull()
    {
        var audio = new ShowAudio { FilePath = P("Old", "song.mp3"), RelativePath = "song.mp3" };

        Assert.Null(ShowAudioPaths.Resolve(audio, ProjectFile, Files(P("Other", "song.mp3"))));
    }

    [Fact]
    public void Resolve_UnsavedProjectAndAbsoluteExists_ReturnsAbsolute()
    {
        var song = P("Music", "song.mp3");

        Assert.Equal(song, ShowAudioPaths.Resolve(new ShowAudio { FilePath = song }, null, Files(song)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_UnsavedProjectAndAbsoluteMissing_ReturnsNull(string? projectFile)
    {
        var audio = new ShowAudio { FilePath = P("Music", "song.mp3"), RelativePath = "song.mp3" };

        Assert.Null(ShowAudioPaths.Resolve(audio, projectFile, _ => false));
    }

    [Fact]
    public void Resolve_EmptyPaths_ReturnsNullWithoutAskingForFolders()
    {
        var asked = new List<string>();
        var audio = new ShowAudio { FilePath = string.Empty, RelativePath = null };

        Assert.Null(ShowAudioPaths.Resolve(audio, ProjectFile, p => { asked.Add(p); return true; }));
        Assert.Empty(asked);
    }

    [Fact]
    public void Resolve_NullFilePathFromBadJson_ReturnsNull()
    {
        var audio = new ShowAudio { FilePath = null!, RelativePath = "  " };

        Assert.Null(ShowAudioPaths.Resolve(audio, ProjectFile, _ => false));
    }

    [Fact]
    public void Resolve_RelativeFilePath_IsNotResolvedAgainstWorkingDirectory()
    {
        var asked = new List<string>();
        var audio = new ShowAudio { FilePath = "song.mp3" };

        ShowAudioPaths.Resolve(audio, ProjectFile, p => { asked.Add(p); return false; });

        Assert.All(asked, p => Assert.StartsWith(ProjectFolder, p, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_PathWithInvalidCharacters_DoesNotThrow()
    {
        var audio = new ShowAudio { FilePath = P("Music", "so\0ng.mp3"), RelativePath = "a\0b.mp3" };

        var result = ShowAudioPaths.Resolve(audio, ProjectFile, _ => false);

        Assert.Null(result);
    }

    [Fact]
    public void Link_SameDrive_SetsAbsoluteAndRelativePath()
    {
        var audio = new ShowAudio();
        var song = Path.Combine(ProjectFolder, "Music", "song.mp3");

        ShowAudioPaths.Link(audio, song, ProjectFile);

        Assert.Equal(song, audio.FilePath);
        Assert.Equal(Path.Combine("Music", "song.mp3"), audio.RelativePath);
    }

    [Fact]
    public void Link_FileOutsideProjectFolder_RelativePathGoesUp()
    {
        var audio = new ShowAudio();

        ShowAudioPaths.Link(audio, P("Songs", "song.mp3"), ProjectFile);

        Assert.Equal(Path.Combine("..", "..", "Songs", "song.mp3"), audio.RelativePath);
    }

    [Fact]
    public void Link_UnsavedProject_RelativePathIsNull()
    {
        var audio = new ShowAudio { RelativePath = "stale.mp3" };

        ShowAudioPaths.Link(audio, P("Songs", "song.mp3"), null);

        Assert.Equal(P("Songs", "song.mp3"), audio.FilePath);
        Assert.Null(audio.RelativePath);
    }

    [Fact]
    public void Link_OtherDrive_RelativePathIsNull()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // drive letters exist only on Windows
        }

        var audio = new ShowAudio();

        ShowAudioPaths.Link(audio, @"D:\Songs\song.mp3", @"C:\Shows\wedding.dmxpro");

        Assert.Equal(@"D:\Songs\song.mp3", audio.FilePath);
        Assert.Null(audio.RelativePath);
    }

    [Fact]
    public void Link_SameDriveDifferentCase_RelativePathIsSet()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var audio = new ShowAudio();

        ShowAudioPaths.Link(audio, @"c:\Shows\Music\song.mp3", @"C:\Shows\wedding.dmxpro");

        Assert.Equal(@"Music\song.mp3", audio.RelativePath);
    }

    [Fact]
    public void Link_PathWithDotSegments_StoresFullPath()
    {
        var audio = new ShowAudio();

        ShowAudioPaths.Link(audio, Path.Combine(ProjectFolder, "x", "..", "song.mp3"), ProjectFile);

        Assert.Equal(Path.Combine(ProjectFolder, "song.mp3"), audio.FilePath);
        Assert.Equal("song.mp3", audio.RelativePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Link_EmptyPath_Throws(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => ShowAudioPaths.Link(new ShowAudio(), path, ProjectFile));
    }

    [Fact]
    public void UpdateRelativePaths_SaveAs_RecomputesFromAbsolutePath()
    {
        var song = P("Songs", "song.mp3");
        var withMusic = ShowWith(song, Path.Combine("..", "..", "Songs", "song.mp3"));
        var noMusic = new Show();

        ShowAudioPaths.UpdateRelativePaths([withMusic, noMusic], P("Songs", "copy.dmxpro"));

        Assert.Equal("song.mp3", withMusic.Audio!.RelativePath);
        Assert.Equal(song, withMusic.Audio.FilePath);
        Assert.Null(noMusic.Audio);
    }

    [Fact]
    public void UpdateRelativePaths_EmptyFilePath_KeepsRelativePath()
    {
        var show = ShowWith(string.Empty, "song.mp3");

        ShowAudioPaths.UpdateRelativePaths([show], P("Other", "p.dmxpro"));

        Assert.Equal("song.mp3", show.Audio!.RelativePath);
    }

    [Fact]
    public void UpdateRelativePaths_WithOldLocation_RelinksSongFoundViaOldRelativePath()
    {
        var actual = Path.Combine(ProjectFolder, "Music", "song.mp3");
        var show = ShowWith(P("Stale", "song.mp3"), Path.Combine("Music", "song.mp3"));

        ShowAudioPaths.UpdateRelativePaths([show], ProjectFile, P("Backup", "wedding.dmxpro"), Files(actual));

        Assert.Equal(actual, show.Audio!.FilePath);
        Assert.Equal(Path.Combine("..", "Shows", "Wedding", "Music", "song.mp3"), show.Audio.RelativePath);
    }

    [Fact]
    public void UpdateRelativePaths_WithOldLocationAndSongMissing_UsesAbsolutePath()
    {
        var show = ShowWith(P("Songs", "song.mp3"), "song.mp3");

        ShowAudioPaths.UpdateRelativePaths([show], ProjectFile, P("Songs", "p.dmxpro"), _ => false);

        Assert.Equal(P("Songs", "song.mp3"), show.Audio!.FilePath);
        Assert.Equal("song.mp3", show.Audio.RelativePath);
    }

    [Fact]
    public void PlanCollect_SongsOutsideProject_CopiesIntoMusicFolder()
    {
        var a = P("Songs", "a.mp3");
        var b = P("Downloads", "b.wav");

        var plan = ShowAudioPaths.PlanCollect([ShowWith(a), ShowWith(b), new Show()], ProjectFile, Files(a, b));

        var music = Path.Combine(ProjectFolder, ShowAudioPaths.MusicFolderName);
        Assert.Equal([(a, Path.Combine(music, "a.mp3")), (b, Path.Combine(music, "b.wav"))], plan);
    }

    [Fact]
    public void PlanCollect_SameSongInSeveralShowsDifferentCase_CopiesOnce()
    {
        var a = P("Songs", "Song.mp3");

        var plan = ShowAudioPaths.PlanCollect(
            [ShowWith(a), ShowWith(P("SONGS", "song.MP3")), ShowWith(a)], ProjectFile, Files(a));

        var copy = Assert.Single(plan);
        Assert.Equal(a, copy.Source);
        Assert.Equal(Path.Combine(ProjectFolder, "Music", "Song.mp3"), copy.Target);
    }

    [Fact]
    public void PlanCollect_DifferentFilesSameName_NumbersTargets()
    {
        var a = P("A", "song.mp3");
        var b = P("B", "song.mp3");
        var c = P("C", "SONG.mp3");

        var plan = ShowAudioPaths.PlanCollect([ShowWith(a), ShowWith(b), ShowWith(c)], ProjectFile, Files(a, b, c));

        var music = Path.Combine(ProjectFolder, "Music");
        Assert.Equal(
            [Path.Combine(music, "song.mp3"), Path.Combine(music, "song (2).mp3"), Path.Combine(music, "SONG (3).mp3")],
            plan.Select(p => p.Target));
    }

    [Fact]
    public void PlanCollect_DifferentFileAlreadyInMusicFolder_AvoidsOverwriting()
    {
        var music = Path.Combine(ProjectFolder, "Music");
        var a = P("A", "song.mp3");

        var plan = ShowAudioPaths.PlanCollect(
            [ShowWith(a)], ProjectFile, Files(a, Path.Combine(music, "song.mp3"), Path.Combine(music, "song (2).mp3")));

        Assert.Equal(Path.Combine(music, "song (3).mp3"), Assert.Single(plan).Target);
    }

    [Fact]
    public void PlanCollect_SongAlreadyInMusicFolder_IsSkipped()
    {
        var collected = Path.Combine(ProjectFolder, "music", "song.mp3");
        var nested = Path.Combine(ProjectFolder, "Music", "Live", "live.mp3");

        var plan = ShowAudioPaths.PlanCollect(
            [ShowWith(collected), ShowWith(nested)], ProjectFile, Files(collected, nested));

        Assert.Empty(plan);
    }

    [Fact]
    public void PlanCollect_SongFoundViaMusicFolderFallback_IsSkipped()
    {
        var collected = Path.Combine(ProjectFolder, "Music", "song.mp3");

        var plan = ShowAudioPaths.PlanCollect([ShowWith(P("Old", "song.mp3"))], ProjectFile, Files(collected));

        Assert.Empty(plan);
    }

    [Fact]
    public void PlanCollect_SongInProjectFolderButNotMusic_IsCollected()
    {
        var inFolder = Path.Combine(ProjectFolder, "song.mp3");

        var plan = ShowAudioPaths.PlanCollect([ShowWith(inFolder)], ProjectFile, Files(inFolder));

        Assert.Equal(Path.Combine(ProjectFolder, "Music", "song.mp3"), Assert.Single(plan).Target);
    }

    [Fact]
    public void PlanCollect_FolderNamedLikeMusicPrefix_IsNotTreatedAsInside()
    {
        var a = Path.Combine(ProjectFolder, "Music2", "song.mp3");

        var plan = ShowAudioPaths.PlanCollect([ShowWith(a)], ProjectFile, Files(a));

        Assert.Single(plan);
    }

    [Fact]
    public void PlanCollect_MissingSong_IsSkipped()
    {
        Assert.Empty(ShowAudioPaths.PlanCollect([ShowWith(P("Gone", "song.mp3"))], ProjectFile, _ => false));
    }

    [Fact]
    public void PlanCollect_EmptyProjectPath_ReturnsEmptyPlan()
    {
        var a = P("A", "song.mp3");

        Assert.Empty(ShowAudioPaths.PlanCollect([ShowWith(a)], string.Empty, Files(a)));
    }

    [Fact]
    public void PlanCollect_FileWithoutExtension_NumbersWithoutExtension()
    {
        var a = P("A", "song");
        var b = P("B", "song");

        var plan = ShowAudioPaths.PlanCollect([ShowWith(a), ShowWith(b)], ProjectFile, Files(a, b));

        Assert.Equal(Path.Combine(ProjectFolder, "Music", "song (2)"), plan[1].Target);
    }

    [Fact]
    public void LinkCollected_AfterCopy_RelinksEveryShowUsingTheSong()
    {
        var a = P("Songs", "a.mp3");
        var show1 = ShowWith(a);
        var show2 = ShowWith(P("songs", "A.mp3"));
        var other = ShowWith(P("Other", "x.mp3"));
        var plan = ShowAudioPaths.PlanCollect([show1, show2], ProjectFile, Files(a));

        var count = ShowAudioPaths.LinkCollected([show1, show2, other, new Show()], plan, ProjectFile, Files(a));

        Assert.Equal(2, count);
        var target = Path.Combine(ProjectFolder, "Music", "a.mp3");
        Assert.Equal(target, show1.Audio!.FilePath);
        Assert.Equal(Path.Combine("Music", "a.mp3"), show1.Audio.RelativePath);
        Assert.Equal(target, show2.Audio!.FilePath);
        Assert.Equal(P("Other", "x.mp3"), other.Audio!.FilePath);
    }
}
