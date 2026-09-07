using System.IO;
using AiPet.AI;

namespace AiPet.ToolWindow;

public sealed partial class HomeViewModel
{
    private bool _aiUserCancelled;
    private IReadOnlyList<AiProviderDescriptor> _customProviders = Array.Empty<AiProviderDescriptor>();
    public bool CanVerifyGeneration => CanTest && _ai is IAiCapabilityClient && SelectedProvider?.SupportsStructuredJson != false;
    public async Task VerifyAiGenerationAsync()
    {
        if (!CanVerifyGeneration) return;
        var task=TestConnectionAsync(generation:true);
        _aiTestTask=task;
        try { await task; }
        finally { if (ReferenceEquals(_aiTestTask,task)) _aiTestTask=null; }
    }
    public void CancelAiVerification() { _aiUserCancelled=true; _aiTestCts?.Cancel(); }
    public void ExportAiConfigurations(string file) => AiConfigurationExchange.Export(file, _settings!.Load().Ai.Profiles);
    public int ImportAiConfigurations(IReadOnlyList<PortableAiConfiguration> preview, AiImportConflict conflict)
    {
        var count=AiConfigurationExchange.Import(_settings!,preview,conflict);
        _loadingAiConfiguration=true;
        try { RefreshSavedAiConfigurations(_settings!.Load().Ai); }
        finally { _loadingAiConfiguration=false; }
        Status=$"已导入 {count} 组配置。活动配置保留；新配置需补充 Key 并验证。";
        return count;
    }
    public void ImportProviderPresets(string file)
    {
        var presets=AiProviderPresetStore.Load(file);
        if (presets.Count>32 || presets.Any(preset=>AiProviders.Builtin.Any(builtin=>builtin.Id==preset.Id))) throw new InvalidDataException("最多 32 个自定义预设，不能覆盖内置 ID。");
        Storage.RecoverableAtomicFile.WriteAllText(Path.Combine(_settings!.AppDataDir,"provider-presets.json"),File.ReadAllText(file));
        _customProviders=presets;
        OnPCFor(nameof(Providers)); OnPCFor(nameof(AiConfigurationTemplates));
        Status="预设已导入，选择模板后可新建配置。";
    }
    private void LoadCustomProviderPresets()
    {
        try { var file=Path.Combine(_settings!.AppDataDir,"provider-presets.json"); if (File.Exists(file)) _customProviders=AiProviderPresetStore.Load(file).Where(preset=>!AiProviders.Builtin.Any(builtin=>builtin.Id==preset.Id)).Take(32).ToArray(); }
        catch { _customProviders=Array.Empty<AiProviderDescriptor>(); }
        OnPCFor(nameof(Providers)); OnPCFor(nameof(AiConfigurationTemplates));
    }
}
