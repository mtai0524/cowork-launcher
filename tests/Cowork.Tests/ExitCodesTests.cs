using Cowork.Core.Models;
using Xunit;

namespace Cowork.Tests;

public class ExitCodesTests
{
    [Theory]
    [InlineData("", "0")]
    [InlineData("   ", "0")]
    [InlineData("0", "0")]
    [InlineData("1, 0", "0, 1")]
    [InlineData("3;1 0", "0, 1, 3")]
    [InlineData("abc, 2", "2")]
    [InlineData("1, 1, 1", "1")]
    [InlineData("-1", "-1")]
    [InlineData("3010,0", "0, 3010")]
    public void Parse_ThenFormat_GivesTheCanonicalList(string input, string expected)
        => Assert.Equal(expected, ExitCodes.Format(ExitCodes.Parse(input)));

    [Fact]
    public void Normalize_NullOrEmpty_FallsBackToZero()
    {
        Assert.Equal(new[] { 0 }, ExitCodes.Normalize(null));
        Assert.Equal(new[] { 0 }, ExitCodes.Normalize(Array.Empty<int>()));
    }

    [Fact]
    public void Normalize_DoesNotAddZero_WhenTheUserLeftItOut()
        => Assert.Equal(new[] { 1 }, ExitCodes.Normalize(new[] { 1 }));

    [Fact]
    public void IsSuccess_MatchesTheConfiguredCodes()
    {
        var codes = new[] { 0, 1 };

        Assert.True(ExitCodes.IsSuccess(codes, 1));
        Assert.False(ExitCodes.IsSuccess(codes, 2));
    }

    [Fact]
    public void IsSuccess_UnknownExitCode_IsAFailure()
        => Assert.False(ExitCodes.IsSuccess(new[] { 0 }, null));
}
