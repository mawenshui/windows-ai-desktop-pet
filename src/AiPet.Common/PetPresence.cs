namespace AiPet.Common;

/// <summary>Human-readable reason and recovery route for the pet's current state.</summary>
public static class PetPresence
{
    public static string Describe(bool userHidden, bool fullscreenHidden, bool visible,
        bool focusActive, bool focusPaused, bool clickThrough)
    {
        if (userHidden) return "桌宠已手动隐藏 · 从托盘选择显示桌宠";
        if (fullscreenHidden) return "同屏全屏中自动隐藏 · 退出全屏后恢复，也可从托盘显示桌宠";
        if (!visible) return "桌宠暂未显示 · 从托盘选择显示桌宠";
        if (clickThrough) return "专注期间点击穿透 · 从托盘恢复桌宠交互";
        if (focusPaused) return "专注已暂停 · 可在待办页或托盘继续";
        if (focusActive) return "专注中，桌宠保持安静 · 可在待办页或托盘暂停";
        return "桌宠可交互 · 点击打开工具窗口";
    }
}
