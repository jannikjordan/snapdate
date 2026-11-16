using System.Globalization;
using Snapdate.Domain.Naming;
using Snapdate.Domain.Photos;

namespace Snapdate.Domain.Tests.Naming;

public sealed class NamePatternTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    // Sunday, 4 July 2024, 09:05:08
    private static PhotoFacts Facts(string? camera = "iPhone 15 Pro", int counter = 7, string name = "IMG_4711") =>
        new(name, ".JPG", new CaptureDate(new DateTime(2024, 7, 4, 9, 5, 8), DateSource.ExifOriginal), camera, counter);

    private static string Render(string pattern, CultureInfo? culture = null, PhotoFacts? facts = null)
    {
        var result = NamePattern.Parse(pattern);
        Assert.True(result.IsValid, $"Pattern '{pattern}' should be valid: {string.Join(", ", result.Errors)}");
        return result.Pattern!.Render(facts ?? Facts(), culture ?? En);
    }

    [Theory]
    [InlineData("{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss}", "2024-07-04_09-05-08")]
    [InlineData("{YYYY}-{MM}-{DD} {HH}{mm}", "2024-07-04 0905")]
    [InlineData("{YYYY}{MM}{DD}_{HH}{mm}{ss}", "20240704_090508")]
    [InlineData("{D}.{M}.{YYYY}", "4.7.2024")]
    [InlineData("{H}h{m}m{s}s", "9h5m8s")]
    [InlineData("{YY}-{MM}", "24-07")]
    [InlineData("Holiday {D}.{M}.{YYYY}", "Holiday 4.7.2024")]
    [InlineData("{YYYY}-{MM}-{DD} {name}", "2024-07-04 IMG_4711")]
    [InlineData("{counter} {camera}", "007 iPhone 15 Pro")]
    public void Renders_tokens_and_keeps_literal_text(string pattern, string expected)
    {
        Assert.Equal(expected, Render(pattern));
    }

    [Fact]
    public void Leading_zero_variants_differ_only_for_single_digits()
    {
        var late = Facts() with { Date = new CaptureDate(new DateTime(2024, 12, 24, 18, 45, 59), DateSource.ExifOriginal) };
        Assert.Equal("12.24 18-45-59", Render("{M}.{D} {H}-{m}-{s}", facts: late));
        Assert.Equal("12.24 18-45-59", Render("{MM}.{DD} {HH}-{mm}-{ss}", facts: late));
    }

    [Theory]
    [InlineData("en-US", "Jul July Thu Thursday")]
    [InlineData("de-DE", "Jul Juli Do Donnerstag")]
    public void Month_and_weekday_names_follow_the_ui_language(string culture, string expected)
    {
        Assert.Equal(expected, Render("{MMM} {MMMM} {ddd} {dddd}", CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void Short_month_name_is_always_three_letters()
    {
        var september = Facts() with { Date = new CaptureDate(new DateTime(2024, 9, 1), DateSource.ExifOriginal) };
        Assert.Equal("Sep", Render("{MMM}", En, september));
        Assert.Equal("Sep", Render("{MMM}", De, september));
    }

    [Fact]
    public void Slash_and_backslash_create_folders()
    {
        Assert.Equal("2024/07/2024-07-04", Render("{YYYY}/{MM}/{YYYY}-{MM}-{DD}"));
        Assert.Equal("2024/07/x", Render(@"{YYYY}\{MM}\x"));
    }

    [Fact]
    public void Doubled_braces_are_literal_braces()
    {
        Assert.Equal("{2024}", Render("{{{YYYY}}}"));
    }

    [Fact]
    public void Placeholders_are_case_sensitive()
    {
        Assert.Equal("07-05", Render("{MM}-{mm}"));
        var result = NamePattern.Parse("{yyyy}");
        Assert.False(result.IsValid);
        Assert.Equal(PatternErrorKind.UnknownPlaceholder, Assert.Single(result.Errors).Kind);
        Assert.Equal("yyyy", result.Errors[0].Detail);
    }

    [Theory]
    [InlineData("", PatternErrorKind.Empty)]
    [InlineData("   ", PatternErrorKind.Empty)]
    [InlineData("{YYYY", PatternErrorKind.UnclosedBrace)]
    [InlineData("YYYY}", PatternErrorKind.UnexpectedClosingBrace)]
    [InlineData("{foo}", PatternErrorKind.UnknownPlaceholder)]
    [InlineData("{YYYY}:{MM}", PatternErrorKind.InvalidCharacter)]
    [InlineData("{YYYY}?", PatternErrorKind.InvalidCharacter)]
    [InlineData("a*b", PatternErrorKind.InvalidCharacter)]
    [InlineData("/{YYYY}", PatternErrorKind.EmptyFolderName)]
    [InlineData("{YYYY}//{MM}", PatternErrorKind.EmptyFolderName)]
    [InlineData("{YYYY}/", PatternErrorKind.EmptyFolderName)]
    public void Invalid_patterns_report_a_clear_error(string pattern, PatternErrorKind expected)
    {
        var result = NamePattern.Parse(pattern);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Kind == expected);
    }

    [Fact]
    public void Error_position_points_at_the_problem()
    {
        var error = Assert.Single(NamePattern.Parse("{YYYY}:x").Errors);
        Assert.Equal(6, error.Position);
        Assert.Equal(":", error.Detail);
    }

    [Fact]
    public void Text_round_trips_through_segments()
    {
        var parsed = NamePattern.Parse(@"{{x}} {YYYY}\{MM} Holiday").Pattern!;
        Assert.Equal("{{x}} {YYYY}/{MM} Holiday", parsed.Text);

        var rebuilt = NamePattern.FromSegments(parsed.Segments);
        Assert.True(rebuilt.IsValid);
        Assert.Equal(parsed.Text, rebuilt.Pattern!.Text);
    }

    [Fact]
    public void Default_pattern_matches_the_documented_default()
    {
        Assert.Equal("{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss}", NamePattern.Default.Text);
    }

    [Fact]
    public void Malicious_camera_metadata_cannot_escape_or_add_folders()
    {
        var evil = Facts(camera: @"..\..\Windows\System32");
        var path = Render("{camera}", facts: evil);
        Assert.DoesNotContain('/', path);
        Assert.DoesNotContain('\\', path);
        Assert.Equal("..-..-Windows-System32", path);
    }

    [Fact]
    public void Camera_metadata_with_control_characters_is_cleaned()
    {
        Assert.Equal("Canon-EOS R6", Render("{camera}", facts: Facts(camera: "Canon\u0000EOS\t R6")));
    }

    [Fact]
    public void Missing_camera_falls_back_to_original_name_instead_of_empty_file_name()
    {
        Assert.Equal("IMG_4711", Render("{camera}", facts: Facts(camera: null)));
        Assert.Equal("2024/IMG_4711", Render("{YYYY}/{camera}", facts: Facts(camera: "  ")));
    }

    [Fact]
    public void Empty_folder_from_missing_value_becomes_placeholder_folder()
    {
        Assert.Equal("_/2024", Render("{camera}/{YYYY}", facts: Facts(camera: null)));
    }

    [Fact]
    public void Rendered_reserved_device_names_are_neutralised()
    {
        Assert.Equal("_CON", Render("{name}", facts: Facts(name: "CON")));
    }
}
