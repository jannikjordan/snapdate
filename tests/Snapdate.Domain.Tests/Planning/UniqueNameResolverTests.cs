using Snapdate.Domain.Planning;

namespace Snapdate.Domain.Tests.Planning;

public sealed class UniqueNameResolverTests
{
    [Fact]
    public void First_use_keeps_the_name()
    {
        var resolver = new UniqueNameResolver(_ => false);
        Assert.Equal("a.jpg", resolver.Reserve("a", ".jpg", out var renamed));
        Assert.False(renamed);
    }

    [Fact]
    public void Duplicates_within_a_plan_get_increasing_numbers()
    {
        var resolver = new UniqueNameResolver(_ => false);
        resolver.Reserve("a", ".jpg", out _);
        Assert.Equal("a (2).jpg", resolver.Reserve("a", ".jpg", out var renamed));
        Assert.True(renamed);
        Assert.Equal("a (3).jpg", resolver.Reserve("a", ".jpg", out _));
    }

    [Fact]
    public void Existing_files_on_disk_are_never_reused()
    {
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.jpg", "a (2).jpg" };
        var resolver = new UniqueNameResolver(onDisk.Contains);
        Assert.Equal("a (3).jpg", resolver.Reserve("a", ".jpg", out _));
    }

    [Fact]
    public void Comparison_ignores_case_like_ntfs()
    {
        var resolver = new UniqueNameResolver(_ => false);
        resolver.Reserve("Photo", ".JPG", out _);
        Assert.Equal("photo (2).jpg", resolver.Reserve("photo", ".jpg", out _));
    }

    [Fact]
    public void Different_extensions_do_not_collide()
    {
        var resolver = new UniqueNameResolver(_ => false);
        resolver.Reserve("a", ".jpg", out _);
        Assert.Equal("a.png", resolver.Reserve("a", ".png", out var renamed));
        Assert.False(renamed);
    }
}
