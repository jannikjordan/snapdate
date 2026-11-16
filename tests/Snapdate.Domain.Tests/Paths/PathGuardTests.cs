using Snapdate.Domain.Paths;

namespace Snapdate.Domain.Tests.Paths;

public sealed class PathGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "snapdate-root");

    [Fact]
    public void Relative_paths_inside_the_root_are_combined()
    {
        var full = PathGuard.CombineInside(Root, "2024/07/a.jpg");
        Assert.Equal(Path.Combine(Root, "2024", "07", "a.jpg"), full);
    }

    [Theory]
    [InlineData("../escape.jpg")]
    [InlineData("a/../../escape.jpg")]
    [InlineData(@"..\escape.jpg")]
    [InlineData(".")]
    public void Paths_escaping_the_root_are_rejected(string relative)
    {
        Assert.Throws<InvalidOperationException>(() => PathGuard.CombineInside(Root, relative));
    }

    [Fact]
    public void Rooted_paths_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => PathGuard.CombineInside(Root, @"C:\Windows\x.jpg"));
    }

    [Fact]
    public void Sibling_folder_with_same_prefix_is_not_inside()
    {
        Assert.False(PathGuard.IsInside(Root, Root + "-other"));
        Assert.True(PathGuard.IsInside(Root, Path.Combine(Root, "sub")));
        Assert.True(PathGuard.IsInside(Root, Root + Path.DirectorySeparatorChar));
    }
}
