using AiPet.Shortcuts;

namespace AiPet.ToolWindow;

public sealed record ShortcutManagerInteractionState(
    string SelectionSummary,
    string GroupLabel,
    string PinLabel,
    bool CanOrganize,
    bool CanRelocate,
    bool CanMoveUp,
    bool CanMoveDown,
    bool CanRemove,
    bool CanScan,
    bool CanCancelScan,
    bool CanUndo);

public static class ShortcutManagerInteraction
{
    public static ShortcutManagerInteractionState Resolve(
        IReadOnlyList<ShortcutItem> displayed,
        IReadOnlyCollection<Guid> selectedIds,
        bool scanning,
        bool canUndo,
        string? groupName)
    {
        var selected = displayed.Where(item => selectedIds.Contains(item.Id)).ToArray();
        var single = selected.Length == 1 ? selected[0] : null;
        var index = single is null ? -1 : FindIndex(displayed, single.Id);
        var canMutate = !scanning && selected.Length > 0;
        var canMoveUp = !scanning && single is not null && index > 0
            && displayed[index - 1].Pinned == single.Pinned;
        var canMoveDown = !scanning && single is not null && index >= 0 && index < displayed.Count - 1
            && displayed[index + 1].Pinned == single.Pinned;

        return new ShortcutManagerInteractionState(
            $"当前显示 {displayed.Count} 个入口 · 已选 {selected.Length} 个",
            string.IsNullOrWhiteSpace(groupName) ? "移出分组" : "移入分组",
            selected.Length > 0 && selected.All(item => item.Pinned) ? "取消固定" : "固定",
            canMutate,
            !scanning && single is not null,
            canMoveUp,
            canMoveDown,
            canMutate,
            !scanning,
            scanning,
            !scanning && canUndo);
    }

    private static int FindIndex(IReadOnlyList<ShortcutItem> items, Guid id)
    {
        for (var index = 0; index < items.Count; index++)
            if (items[index].Id == id) return index;
        return -1;
    }
}
