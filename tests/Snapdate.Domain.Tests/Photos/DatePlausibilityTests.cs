using Snapdate.Domain.Photos;

namespace Snapdate.Domain.Tests.Photos;

public sealed class DatePlausibilityTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 8, 6, 0);

    [Theory]
    [InlineData("1900-01-01 00:00:00")]
    [InlineData("2016-07-06 00:00:00")]
    [InlineData("2026-11-18 08:06:00")]
    [InlineData("2026-11-20 08:06:00")]
    public void Dates_from_1900_up_to_two_days_ahead_are_plausible(string value)
    {
        Assert.True(DatePlausibility.IsPlausible(DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Now));
    }

    [Theory]
    [InlineData("1899-12-31 23:59:59")]
    [InlineData("0001-01-01 00:00:00")]
    [InlineData("2026-11-20 08:06:01")]
    [InlineData("2099-12-31 00:00:00")]
    public void Unset_clocks_and_future_dates_are_not_plausible(string value)
    {
        Assert.False(DatePlausibility.IsPlausible(DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Now));
    }

    [Fact]
    public void Date_kind_is_ignored()
    {
        var utc = DateTime.SpecifyKind(new DateTime(2026, 11, 20, 8, 6, 0), DateTimeKind.Utc);
        Assert.True(DatePlausibility.IsPlausible(utc, Now));
    }

    [Fact]
    public void Earliest_is_the_first_of_january_1900()
    {
        Assert.Equal(new DateTime(1900, 1, 1), DatePlausibility.Earliest);
    }
}
