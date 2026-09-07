using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiPet.Shortcuts;

public sealed record ShortcutRemovalPreview(IReadOnlyList<ShortcutItem> Items,string Fingerprint);
public sealed partial class ShortcutStore
{
    private IReadOnlyList<ShortcutItem> _removedForUndo = Array.Empty<ShortcutItem>();
    public bool CanUndoRemoval => _removedForUndo.Count>0;
    private static string Fingerprint(IEnumerable<ShortcutItem> items) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items.OrderBy(item=>item.Id),Options))));
    public ShortcutRemovalPreview PreviewRemoval(IEnumerable<Guid> ids)
    {
        var selected=ids.ToHashSet(); var items=Load().Where(item=>selected.Contains(item.Id)).ToArray();
        return new(items,Fingerprint(items));
    }
    public int RemoveMany(ShortcutRemovalPreview preview)
    {
        var current=Load().ToList(); var ids=preview.Items.Select(item=>item.Id).ToHashSet();
        var removed=current.Where(item=>ids.Contains(item.Id)).ToArray();
        if (Fingerprint(removed)!=preview.Fingerprint) throw new InvalidOperationException("快捷入口已变化，请重新预览。");
        Save(current.Where(item=>!ids.Contains(item.Id)));
        _removedForUndo=removed;
        return removed.Length;
    }
    public int UndoRemoval()
    {
        var current=Load().ToList(); var ids=current.Select(item=>item.Id).ToHashSet(); var added=0;
        foreach (var item in _removedForUndo.OrderBy(item=>item.Order))
            if (ids.Add(item.Id)) { current.Insert(Math.Clamp(item.Order,0,current.Count),item); added++; }
        for (var i=0;i<current.Count;i++) current[i].Order=i;
        Save(current); _removedForUndo=Array.Empty<ShortcutItem>(); return added;
    }
    public void Organize(IEnumerable<Guid> ids,string? group=null,bool? pinned=null)
    {
        if (group?.Length>40) throw new InvalidOperationException("分组名称最多 40 字。");
        var selected=ids.ToHashSet(); var items=Load();
        foreach (var item in items.Where(item=>selected.Contains(item.Id)))
        { if (group is not null) item.Group=group.Trim(); if (pinned is not null) item.Pinned=pinned.Value; item.UpdatedAt=DateTimeOffset.UtcNow; }
        Save(items);
    }
    public Task<IReadOnlyList<Guid>> ScanInvalidAsync(CancellationToken ct=default)
    {
        var snapshot=Load();
        return Task.Run<IReadOnlyList<Guid>>(()=>
        {
            var invalid=new List<Guid>();
            foreach (var item in snapshot) { ct.ThrowIfCancellationRequested(); if (!item.ExistsNow) invalid.Add(item.Id); }
            ct.ThrowIfCancellationRequested(); return invalid;
        },ct);
    }
}
