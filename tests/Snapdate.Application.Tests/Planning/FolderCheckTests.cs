using Snapdate.Application.Planning;
using Snapdate.Application.Tests.Fakes;

namespace Snapdate.Application.Tests.Planning;

public sealed class FolderCheckTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "fc");
    private static readonly string Source = Path.Combine(Base, "photos");

    private static FakeFileSystem Fs()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Source);
        return fs;
    }

    [Fact]
    public void Missing_folders_are_reported_in_order()
    {
        Assert.Equal(FolderProblem.SourceMissing, FolderCheck.Check(Fs(), null, null, true));
        Assert.Equal(FolderProblem.SourceNotFound, FolderCheck.Check(Fs(), Path.Combine(Base, "nope"), null, true));
        Assert.Equal(FolderProblem.TargetMissing, FolderCheck.Check(Fs(), Source, " ", true));
    }

    [Fact]
    public void Separate_folders_are_fine()
    {
        Assert.Equal(FolderProblem.None, FolderCheck.Check(Fs(), Source, Path.Combine(Base, "sorted"), true));
    }

    [Fact]
    public void Same_folder_is_allowed_for_renaming_in_place()
    {
        Assert.Equal(FolderProblem.None, FolderCheck.Check(Fs(), Source, Source + Path.DirectorySeparatorChar, true));
    }

    [Fact]
    public void Target_inside_source_is_blocked_only_when_scanning_sub_folders()
    {
        var inside = Path.Combine(Source, "sorted");
        Assert.Equal(FolderProblem.TargetInsideSource, FolderCheck.Check(Fs(), Source, inside, recursive: true));
        Assert.Equal(FolderProblem.None, FolderCheck.Check(Fs(), Source, inside, recursive: false));
    }

    [Fact]
    public void Source_inside_target_is_blocked()
    {
        Assert.Equal(FolderProblem.SourceInsideTarget, FolderCheck.Check(Fs(), Source, Base, recursive: false));
    }
}
