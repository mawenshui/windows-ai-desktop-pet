using System.Text.Json;

namespace AiPet.Storage;

public sealed partial class DataMaintenanceService
{
    /// <summary>Offline, explicitly confirmed uninstall path. Only known application data is removed.</summary>
    public DataMaintenanceResult RemoveApplicationData(Action<string> deleteCredential)
    {
        ArgumentNullException.ThrowIfNull(deleteCredential);
        CheckNoLinks(_root); CheckNoLinks(_logs);
        var settings=Path.Combine(_root,"settings.json");
        var targets=new HashSet<string>(StringComparer.Ordinal)
        {
            // This target is fixed by the updater and therefore is not stored
            // in settings, but it is still known application-owned data.
            "WindowsAiDesktopPet:Updates:GitHub",
        };
        if(File.Exists(settings))
        {
            using var json=JsonDocument.Parse(File.ReadAllBytes(settings));
            if(json.RootElement.TryGetProperty("ai",out var ai))
            {
                void Add(JsonElement element)
                {
                    if(element.TryGetProperty("secretTargetName",out var value) && value.GetString() is { } target)
                    {
                        if(!target.StartsWith("WindowsAiDesktopPet:AI:",StringComparison.Ordinal)) throw new InvalidDataException("非本应用凭据引用，停止清理。");
                        targets.Add(target);
                    }
                }
                Add(ai);
                if(ai.TryGetProperty("profiles",out var profiles)) foreach(var profile in profiles.EnumerateArray()) Add(profile);
            }
        }
        foreach(var target in targets) deleteCredential(target);
        var result=Reset(DataModule.AllNonSecret);
        var errors=result.Errors.ToList();
        try
        {
            DeleteTree(WorkRoot);
            DeleteTree(Path.Combine(_root,"automatic-backups"));
            foreach(var filename in ModulePaths.Values.Where(name=>name.EndsWith(".json",StringComparison.Ordinal)))
            foreach(var suffix in new[]{".previous",".pre-v3.bak",".pre-v3.bak.previous",".pre-v4.bak",".pre-v4.bak.previous",".pre-v5.bak",".pre-v5.bak.previous",".pre-v6.bak",".pre-v6.bak.previous"}) DeleteTree(Path.Combine(_root,filename+suffix));
        }
        catch { errors.Add("cleanup_backups_failed"); }
        return new(result.Completed,errors);
    }
}
