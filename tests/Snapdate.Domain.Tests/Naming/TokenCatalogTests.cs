using Snapdate.Domain.Naming;

namespace Snapdate.Domain.Tests.Naming;

public sealed class TokenCatalogTests
{
    [Fact]
    public void Every_token_kind_has_exactly_one_name()
    {
        foreach (var kind in Enum.GetValues<TokenKind>())
        {
            var name = TokenCatalog.NameOf(kind);
            Assert.True(TokenCatalog.TryGet(name, out var back));
            Assert.Equal(kind, back);
        }

        Assert.Equal(Enum.GetValues<TokenKind>().Length, TokenCatalog.Names.Count());
    }

    [Theory]
    [InlineData(TokenKind.Month2, TokenKind.Month1)]
    [InlineData(TokenKind.Day2, TokenKind.Day1)]
    [InlineData(TokenKind.Hour2, TokenKind.Hour1)]
    [InlineData(TokenKind.Minute2, TokenKind.Minute1)]
    [InlineData(TokenKind.Second2, TokenKind.Second1)]
    public void Leading_zero_variants_are_paired_both_ways(TokenKind withZero, TokenKind withoutZero)
    {
        Assert.Equal(withoutZero, TokenCatalog.LeadingZeroCounterpart(withZero));
        Assert.Equal(withZero, TokenCatalog.LeadingZeroCounterpart(withoutZero));
        Assert.True(TokenCatalog.HasLeadingZero(withZero));
        Assert.False(TokenCatalog.HasLeadingZero(withoutZero));
    }

    [Theory]
    [InlineData(TokenKind.Year4)]
    [InlineData(TokenKind.OriginalName)]
    [InlineData(TokenKind.Counter)]
    public void Tokens_without_variants_have_no_counterpart(TokenKind kind)
    {
        Assert.Null(TokenCatalog.LeadingZeroCounterpart(kind));
    }

    [Fact]
    public void Every_alternate_form_switches_back()
    {
        foreach (var kind in Enum.GetValues<TokenKind>())
        {
            if (TokenCatalog.Alternate(kind) is { } other)
            {
                Assert.Equal(kind, TokenCatalog.Alternate(other));
            }
        }

        Assert.Equal(TokenKind.MonthNameLong, TokenCatalog.Alternate(TokenKind.MonthNameShort));
        Assert.Equal(TokenKind.Year2, TokenCatalog.Alternate(TokenKind.Year4));
        Assert.Null(TokenCatalog.Alternate(TokenKind.Camera));
    }
}
