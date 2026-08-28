using System;

namespace AiPet.ToolWindow;

public static class ShellNavigation
{
    public static int NextTabIndex(int currentIndex, int tabCount, bool reverse)
    {
        if (tabCount <= 0) throw new ArgumentOutOfRangeException(nameof(tabCount));
        if (currentIndex < 0 || currentIndex >= tabCount) currentIndex = 0;

        var delta = reverse ? -1 : 1;
        return (currentIndex + delta + tabCount) % tabCount;
    }
}
