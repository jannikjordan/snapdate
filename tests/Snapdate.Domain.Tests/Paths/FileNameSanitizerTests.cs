using Snapdate.Domain.Paths;

namespace Snapdate.Domain.Tests.Paths;

public sealed class FileNameSanitizerTests
{
    [Theory]
    [InlineData("normal name", "normal name")]
    [InlineData("a:b*c?d\"e<f>g|h", "a-b-c-d-e-f-g-h")]
    [InlineData("trailing dots...", "trailing dots")]
    [InlineData("  padded  ", "padded")]
    [InlineData("", "_")]
    [InlineData("   ", "_")]
    [InlineData("..", "_")]
    [InlineData(".", "_")]
    [InlineData("con", "_con")]
    [InlineData("NUL.txt", "_NUL.txt")]
    [InlineData("COM1", "_COM1")]
    [InlineData("LPT9", "_LPT9")]
    [InlineData("Console", "Console")]
    [InlineData("tab\there", "tab-here")]
    public void Segments_are_made_safe_for_windows(string input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.SanitizeSegment(input));
    }

    [Fact]
    public void Overlong_segments_are_truncated()
    {
        var result = FileNameSanitizer.SanitizeSegment(new string('a', 500));
        Assert.Equal(FileNameSanitizer.MaxSegmentLength, result.Length);
    }

    [Theory]
    [InlineData("iPhone 15 Pro", "iPhone 15 Pro")]
    [InlineData("  Canon   EOS  R6 ", "Canon EOS R6")]
    [InlineData("a/b\\c", "a-b-c")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Values_never_contain_separators_and_collapse_whitespace(string? input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.SanitizeValue(input));
    }
}
