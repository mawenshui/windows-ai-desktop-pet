using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class ShellNavigationTests
{
    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(3, false, 0)]
    [InlineData(0, true, 3)]
    [InlineData(2, true, 1)]
    public void Cycles_tabs_in_both_directions(int current, bool reverse, int expected)
    {
        Assert.Equal(expected, ShellNavigation.NextTabIndex(current, 4, reverse));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 3)]
    [InlineData(0, -1)]
    [InlineData(5, -1)]
    public void Maps_alt_digit_shortcuts_without_wrapping(int digit, int expected)
    {
        Assert.Equal(expected, ShellNavigation.DirectTabIndex(digit, 4));
    }
}
