using System.Collections;
using System.Globalization;
using System.Resources;
using Snapdate.App.Localization;

namespace Snapdate.App.Tests;

public sealed class LocalizationTests
{
    private static readonly ResourceManager Resources =
        new("Snapdate.App.Localization.Strings", typeof(LocalizedStrings).Assembly);

    private static HashSet<string> KeysFor(CultureInfo culture)
    {
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for '{culture.Name}'.");
        return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> TypedProperties() =>
        typeof(LocalizedStrings).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void German_has_exactly_the_same_keys_as_English()
    {
        var english = KeysFor(CultureInfo.InvariantCulture);
        var german = KeysFor(CultureInfo.GetCultureInfo("de"));

        Assert.Empty(english.Except(german));
        Assert.Empty(german.Except(english));
    }

    [Fact]
    public void Every_resource_key_has_a_typed_property_and_vice_versa()
    {
        var keys = KeysFor(CultureInfo.InvariantCulture);
        var properties = TypedProperties();

        Assert.Empty(keys.Except(properties));
        Assert.Empty(properties.Except(keys));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Every_string_resolves_to_non_empty_text(string culture)
    {
        var strings = new LocalizedStrings(CultureInfo.GetCultureInfo(culture));
        foreach (var key in TypedProperties())
        {
            var value = strings.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(value), $"'{key}' is empty for {culture}.");
            Assert.NotEqual(key, value);
        }
    }

    [Fact]
    public void German_strings_differ_from_English()
    {
        var en = new LocalizedStrings(CultureInfo.GetCultureInfo("en-US"));
        var de = new LocalizedStrings(CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal("Choose folders", en.Step1Title);
        Assert.Equal("Ordner wählen", de.Step1Title);
    }
}
