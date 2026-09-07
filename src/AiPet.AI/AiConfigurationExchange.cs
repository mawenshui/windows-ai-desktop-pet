using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiPet.Storage;

namespace AiPet.AI;

public sealed record PortableAiConfiguration(string Name, string ProviderId, string Endpoint, string Model);
public sealed record AiConfigurationExchangeDocument(int SchemaVersion, IReadOnlyList<PortableAiConfiguration> Configurations);
public enum AiImportConflict { Skip, Rename }

public static class AiConfigurationExchange
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive=true, WriteIndented=true, UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow };
    public static IReadOnlyList<PortableAiConfiguration> Preview(string file)
    {
        if (new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("配置文件最大 1 MB。");
        var document=JsonSerializer.Deserialize<AiConfigurationExchangeDocument>(File.ReadAllText(file),Options) ?? throw new InvalidDataException("配置文件为空。");
        if (document.SchemaVersion!=1 || document.Configurations is null || document.Configurations.Count>100) throw new InvalidDataException("配置格式或数量无效。");
        foreach (var item in document.Configurations)
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length>100 || string.IsNullOrWhiteSpace(item.Model) || item.Model.Length>200 || string.IsNullOrWhiteSpace(item.ProviderId) || item.ProviderId.Length>100 || !OpenAiCompatibleClient.IsValidEndpoint(item.Endpoint))
                throw new InvalidDataException("配置名称、模型或端点无效。不能包含凭据和查询参数。");
        return document.Configurations;
    }
    public static void Export(string file, IEnumerable<AiConfigurationProfile> profiles) => RecoverableAtomicFile.WriteAllText(file,
        JsonSerializer.Serialize(new AiConfigurationExchangeDocument(1, profiles.Select(profile => new PortableAiConfiguration(profile.DisplayName,profile.ProviderId,profile.Endpoint,profile.Model)).ToArray()),Options));

    public static int Import(SettingsStore store, IReadOnlyList<PortableAiConfiguration> preview, AiImportConflict conflict)
    {
        if(!Enum.IsDefined(conflict) || preview.Count>100 || preview.Any(item=>item is null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length>100 || string.IsNullOrWhiteSpace(item.Model) || item.Model.Length>200 || string.IsNullOrWhiteSpace(item.ProviderId) || item.ProviderId.Length>100 || !OpenAiCompatibleClient.IsValidEndpoint(item.Endpoint)))
            throw new InvalidDataException("导入配置无效。");
        var settings=store.Load();
        if (settings.Ai.ActiveProfileId is null) settings.Ai.RequireExplicitActivation=true;
        var names=settings.Ai.Profiles.Select(profile=>profile.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added=0;
        foreach (var item in preview)
        {
            var name=item.Name.Trim();
            if (names.Contains(name) && conflict==AiImportConflict.Skip) continue;
            var suffix=2;
            while (names.Contains(name)) name=item.Name.Trim()[..Math.Min(item.Name.Trim().Length,90)]+$" ({suffix++})";
            names.Add(name);
            var id=Guid.NewGuid().ToString("N");
            settings.Ai.Profiles.Add(new AiConfigurationProfile { Id=id,DisplayName=name,ProviderId=item.ProviderId,Endpoint=item.Endpoint,Model=item.Model,SecretTargetName=$"WindowsAiDesktopPet:AI:profile:{id}",LastStatus="Untested" });
            added++;
        }
        if(settings.Ai.Profiles.Count>100) throw new InvalidDataException("已保存配置最多 100 组，请先整理现有配置。");
        store.Save(settings);
        return added;
    }
}
