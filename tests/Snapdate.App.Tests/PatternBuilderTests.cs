using System.Globalization;
using Snapdate.App.Localization;
using Snapdate.App.ViewModels;

namespace Snapdate.App.Tests;

public sealed class PatternBuilderTests
{
    private static PatternBuilderViewModel Create(string text = "{YYYY}-{MM}") =>
        new(new LocalizedStrings(CultureInfo.GetCultureInfo("en-US")), text);

    [Fact]
    public void Blocks_are_inserted_at_the_caret()
    {
        var vm = Create("{YYYY}{DD}");
        vm.CaretIndex = 6;

        vm.ChipGroups[0].Chips.First(c => c.Label == "Month").InsertCommand.Execute(null);

        Assert.Equal("{YYYY}{MM}{DD}", vm.Text);
        Assert.Equal(10, vm.CaretIndex);
    }

    [Fact]
    public void Pills_mirror_the_pattern_with_friendly_labels_and_sample_values()
    {
        var vm = Create("{YYYY}-{MM}");

        Assert.Equal(3, vm.Pills.Count);
        Assert.Equal("Year", vm.Pills[0].Label);
        Assert.Equal("2024", vm.Pills[0].Value);
        Assert.False(vm.Pills[1].IsToken);
        Assert.Equal("07", vm.Pills[2].Value);
    }

    [Fact]
    public void Clicking_a_pill_switches_its_form()
    {
        var vm = Create("{MM} {MMM} {YYYY}");

        vm.Pills[0].ToggleCommand.Execute(null);
        Assert.Equal("{M} {MMM} {YYYY}", vm.Text);

        vm.Pills[2].ToggleCommand.Execute(null);
        Assert.Equal("{M} {MMMM} {YYYY}", vm.Text);

        vm.Pills[4].ToggleCommand.Execute(null);
        Assert.Equal("{M} {MMMM} {YY}", vm.Text);
    }

    [Fact]
    public void Removing_a_pill_removes_it_from_the_text()
    {
        var vm = Create("{YYYY}-{MM}");
        vm.Pills[1].RemoveCommand.Execute(null);
        Assert.Equal("{YYYY}{MM}", vm.Text);
    }

    [Fact]
    public void Leading_zero_switch_converts_the_whole_pattern()
    {
        var vm = Create("{DD}.{MM}.{YYYY} {HH}:{mm}".Replace(":", "-", StringComparison.Ordinal));
        Assert.True(vm.LeadingZeros);

        vm.LeadingZeros = false;

        Assert.Equal("{D}.{M}.{YYYY} {H}-{m}", vm.Text);
        Assert.Equal("{D}", vm.ChipGroups[0].Chips.First(c => c.Label == "Day").Tooltip.Split(' ')[0]);
    }

    [Fact]
    public void Typed_free_text_is_kept_and_errors_are_explained()
    {
        var vm = Create();
        vm.Text = "Holiday {D}.{M}.{YYYY}";
        Assert.False(vm.HasError);
        Assert.Equal("Holiday", vm.Pills[0].Value.Replace("␣", string.Empty, StringComparison.Ordinal));

        vm.Text = "{YYYY}?";
        Assert.True(vm.HasError);
        Assert.Contains("?", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_messages_with_braces_render_literally()
    {
        var vm = Create();
        vm.Text = "{YYYY";
        Assert.Equal("A “{” is missing its closing “}”.", vm.ErrorMessage);
    }

    [Fact]
    public void Reset_restores_the_default_pattern()
    {
        var vm = Create("{name}");
        vm.ResetCommand.Execute(null);
        Assert.Equal("{YYYY}-{MM}-{DD}_{HH}-{mm}-{ss}", vm.Text);
    }
}
