using HaloDeck.Core.Services;
using Xunit;

namespace HaloDeck.Tests;

public sealed class AccessibilityOskStateCleanerTests
{
    [Fact]
    public void NullOrEmptyInput_ReturnsEmptyAndUnchanged()
    {
        Assert.Equal(string.Empty, AccessibilityOskStateCleaner.RemoveOskFromConfiguration(null, out bool changedNull));
        Assert.False(changedNull);

        Assert.Equal(string.Empty, AccessibilityOskStateCleaner.RemoveOskFromConfiguration(string.Empty, out bool changedEmpty));
        Assert.False(changedEmpty);
    }

    [Fact]
    public void SoloOsk_AnyCase_RemovesTokenAndMarksChanged()
    {
        Assert.Equal(string.Empty, AccessibilityOskStateCleaner.RemoveOskFromConfiguration("osk", out bool changedLower));
        Assert.True(changedLower);

        Assert.Equal(string.Empty, AccessibilityOskStateCleaner.RemoveOskFromConfiguration("OSK", out bool changedUpper));
        Assert.True(changedUpper);
    }

    [Fact]
    public void MultipleToolsWithOsk_RemovesOnlyOskAndPreservesOrder()
    {
        string result = AccessibilityOskStateCleaner.RemoveOskFromConfiguration("narrator,osk,magnifier", out bool changed);
        Assert.True(changed);
        Assert.Equal("narrator,magnifier", result);
    }

    [Fact]
    public void WhitespacePaddedTokensWithOsk_TrimsAndRemovesOskCleanly()
    {
        string result = AccessibilityOskStateCleaner.RemoveOskFromConfiguration(" narrator , osk ", out bool changed);
        Assert.True(changed);
        Assert.Equal("narrator", result);
    }

    [Fact]
    public void ConfigurationWithoutOsk_ReturnsOriginalAndUnchanged()
    {
        const string input = "narrator,magnifier";
        string result = AccessibilityOskStateCleaner.RemoveOskFromConfiguration(input, out bool changed);
        Assert.False(changed);
        Assert.Equal(input, result);
    }
}
