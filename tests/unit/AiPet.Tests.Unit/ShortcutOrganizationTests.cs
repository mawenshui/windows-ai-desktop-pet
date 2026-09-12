using System.IO;
using AiPet.Shortcuts;
using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class ShortcutOrganizationTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"aipet-shortcut-organize-"+Guid.NewGuid().ToString("N"));
    public ShortcutOrganizationTests()=>Directory.CreateDirectory(_root);

    [Fact]
    public void Manager_actions_follow_selection_boundaries_and_scan_state()
    {
        var first = new ShortcutItem { DisplayName = "first", Pinned = true };
        var second = new ShortcutItem { DisplayName = "second", Pinned = true };
        var third = new ShortcutItem { DisplayName = "third", Pinned = false };
        var displayed = new[] { first, second, third };

        var empty = ShortcutManagerInteraction.Resolve(displayed, Array.Empty<Guid>(), false, false, "");
        Assert.Equal("当前显示 3 个入口 · 已选 0 个", empty.SelectionSummary);
        Assert.False(empty.CanOrganize);
        Assert.True(empty.CanScan);
        Assert.Equal("移出分组", empty.GroupLabel);

        var firstSelected = ShortcutManagerInteraction.Resolve(displayed, new[] { first.Id }, false, true, "工作");
        Assert.False(firstSelected.CanMoveUp);
        Assert.True(firstSelected.CanMoveDown);
        Assert.True(firstSelected.CanRelocate);
        Assert.True(firstSelected.CanUndo);
        Assert.Equal("取消固定", firstSelected.PinLabel);
        Assert.Equal("移入分组", firstSelected.GroupLabel);

        var secondSelected = ShortcutManagerInteraction.Resolve(displayed, new[] { second.Id }, false, false, "");
        Assert.True(secondSelected.CanMoveUp);
        Assert.False(secondSelected.CanMoveDown);

        var multiple = ShortcutManagerInteraction.Resolve(displayed, new[] { first.Id, second.Id }, false, false, "");
        Assert.True(multiple.CanOrganize);
        Assert.True(multiple.CanRemove);
        Assert.False(multiple.CanRelocate);
        Assert.False(multiple.CanMoveUp);
        Assert.False(multiple.CanMoveDown);

        var scanning = ShortcutManagerInteraction.Resolve(displayed, new[] { first.Id }, true, true, "工作");
        Assert.False(scanning.CanOrganize);
        Assert.False(scanning.CanScan);
        Assert.False(scanning.CanUndo);
        Assert.True(scanning.CanCancelScan);
    }
    [Fact]
    public void Repeated_reorder_ids_never_duplicate_records()
    {
        var store=new ShortcutStore(_root);
        var one=store.Add(new() {TargetPath=Path.Combine(_root,"one"),DisplayName="one"});
        var two=store.Add(new() {TargetPath=Path.Combine(_root,"two"),DisplayName="two"});
        store.Reorder(new[] {two.Id,two.Id,one.Id});
        Assert.Equal(new[] {two.Id,one.Id},store.Load().Select(item=>item.Id));
    }
    [Fact]
    public async Task Hundred_entries_keep_groups_and_order_and_batch_undo_preserves_original_files_and_icons()
    {
        var store=new ShortcutStore(_root); Directory.CreateDirectory(store.IconsDir);
        var icon=Path.Combine(store.IconsDir,"custom.png"); File.WriteAllText(icon,"fixture");
        var items=Enumerable.Range(0,100).Select(number=>new ShortcutItem {DisplayName=$"item {number}",TargetPath=Path.Combine(_root,$"item-{number}.txt"),Order=number,IconPath=icon}).ToArray();
        foreach (var item in items) File.WriteAllText(item.TargetPath,"fixture"); store.Save(items);
        store.Organize(items.Take(20).Select(item=>item.Id),"work",true);
        store.Reorder(items.AsEnumerable().Reverse().Select(item=>item.Id));
        var restarted=new ShortcutStore(_root); Assert.Equal(100,restarted.Load().Count); Assert.Equal("work",restarted.Load()[0].Group); Assert.True(restarted.Load()[0].Pinned);
        var preview=store.PreviewRemoval(items.Take(50).Select(item=>item.Id)); Assert.Equal(50,store.RemoveMany(preview));
        Assert.All(items,item=>Assert.True(File.Exists(item.TargetPath)));
        store.CleanupUnreferencedIcons(); Assert.True(File.Exists(icon));
        Assert.Equal(50,store.UndoRemoval()); Assert.Equal(100,store.Load().Count);
        File.Delete(items[0].TargetPath);
        Assert.Equal(items[0].Id,Assert.Single(await store.ScanInvalidAsync()));
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.ScanInvalidAsync(cancellation.Token));
    }
    [Fact]
    public void Stale_batch_preview_is_rejected_without_removing_changed_entry()
    {
        var store=new ShortcutStore(_root); var item=store.Add(new(){DisplayName="original",TargetPath="fixture"});
        var preview=store.PreviewRemoval(new[]{item.Id}); item.DisplayName="changed"; store.Update(item);
        Assert.Throws<InvalidOperationException>(()=>store.RemoveMany(preview)); Assert.Single(store.Load());
    }
    public void Dispose()=>Directory.Delete(_root,true);
}
