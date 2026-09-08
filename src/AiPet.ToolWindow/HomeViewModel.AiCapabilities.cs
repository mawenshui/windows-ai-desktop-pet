using System.IO;
using AiPet.AI;
using AiPet.Storage;

namespace AiPet.ToolWindow;

public sealed record CompleteAiImportResult(
    int ConfigurationCount,
    int ConfigurationWithKeyCount,
    int CustomProviderCount,
    string ActiveConfigurationName,
    bool OldCredentialCleanupIncomplete);

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

    public void ExportCompleteAiConfigurations(string file, string password)
    {
        var settings = _settings!.Load();
        if (settings.Ai.Profiles.Count == 0) throw new InvalidOperationException("没有可导出的已保存 AI 配置。");
        var active = settings.Ai.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.Ai.ActiveProfileId, StringComparison.Ordinal))
            ?? settings.Ai.Profiles[0];
        var configurations = settings.Ai.Profiles.Select(profile => new CompleteAiConfiguration(
            profile.Id,
            profile.DisplayName,
            profile.ProviderId,
            profile.Endpoint,
            profile.Model,
            string.IsNullOrWhiteSpace(profile.SecretTargetName) ? string.Empty : _secretStore.Load(profile.SecretTargetName) ?? string.Empty,
            profile.LastStatus,
            profile.LastVerifiedAt)).ToArray();
        var document = new CompleteAiConfigurationDocument(
            1,
            SoftwareVersion,
            DateTimeOffset.UtcNow,
            settings.Ai.RequireExplicitActivation,
            settings.Features.EnableCustomProviderPresets,
            active.Id,
            configurations,
            _customProviders);
        Storage.RecoverableAtomicFile.WriteAllText(file, EncryptedAiConfigurationBundle.Encrypt(document, password));
        Status = $"已导出 {configurations.Length} 组完整 AI 配置；文件已加密。";
    }

    public CompleteAiConfigurationDocument ReadCompleteAiConfigurations(string file, string password)
    {
        var info = new FileInfo(file);
        if (info.Length > EncryptedAiConfigurationBundle.MaximumFileBytes)
            throw new InvalidDataException("完整 AI 配置包最大 4 MB。");
        return EncryptedAiConfigurationBundle.Decrypt(File.ReadAllText(file), password);
    }

    public CompleteAiImportResult ImportCompleteAiConfigurations(CompleteAiConfigurationDocument document)
    {
        var preview = EncryptedAiConfigurationBundle.Preview(document);
        var originalSettings = _settings!.Load();
        var settings = _settings.Load();
        var oldCredentialTargets = settings.Ai.Profiles
            .Select(profile => profile.SecretTargetName)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var presetFile = Path.Combine(_settings.AppDataDir, "provider-presets.json");
        var hadPresetFile = File.Exists(presetFile);
        var previousPresetContents = hadPresetFile ? File.ReadAllText(presetFile) : null;
        var idMap = document.Configurations.ToDictionary(
            configuration => configuration.Id,
            _ => Guid.NewGuid().ToString("N"),
            StringComparer.Ordinal);
        var importedProfiles = document.Configurations.Select(configuration =>
        {
            var id = idMap[configuration.Id];
            return new AiConfigurationProfile
            {
                Id = id,
                DisplayName = configuration.Name.Trim(),
                ProviderId = configuration.ProviderId,
                Endpoint = configuration.Endpoint,
                Model = configuration.Model.Trim(),
                SecretTargetName = $"WindowsAiDesktopPet:AI:profile:{id}",
                LastStatus = configuration.LastStatus,
                LastVerifiedAt = configuration.LastVerifiedAt,
            };
        }).ToArray();
        var newlyWrittenTargets = new List<string>();
        var settingsWriteAttempted = false;
        try
        {
            for (var index = 0; index < importedProfiles.Length; index++)
            {
                var secret = document.Configurations[index].ApiKey;
                if (string.IsNullOrEmpty(secret)) continue;
                _secretStore.Save(importedProfiles[index].SecretTargetName, secret);
                newlyWrittenTargets.Add(importedProfiles[index].SecretTargetName);
            }

            if (document.CustomProviders.Count == 0)
            {
                if (File.Exists(presetFile)) File.Delete(presetFile);
            }
            else
            {
                AiProviderPresetStore.Save(presetFile, document.CustomProviders);
            }

            settings.Ai.Profiles = importedProfiles.ToList();
            settings.Ai.ActiveProfileId = idMap[document.ActiveConfigurationId];
            settings.Ai.RequireExplicitActivation = document.RequireExplicitActivation;
            settings.Features.EnableCustomProviderPresets = document.EnableCustomProviderPresets;
            CopyProfileToActiveAiSettings(
                importedProfiles.Single(profile => string.Equals(profile.Id, settings.Ai.ActiveProfileId, StringComparison.Ordinal)),
                settings.Ai);
            settingsWriteAttempted = true;
            _settings.Save(settings);
        }
        catch (Exception ex)
        {
            var rollbackIncomplete = false;
            foreach (var target in newlyWrittenTargets)
            {
                try { _secretStore.Delete(target); } catch { rollbackIncomplete = true; }
            }
            try
            {
                if (hadPresetFile) Storage.RecoverableAtomicFile.WriteAllText(presetFile, previousPresetContents!);
                else if (File.Exists(presetFile)) File.Delete(presetFile);
            }
            catch { rollbackIncomplete = true; }
            if (settingsWriteAttempted)
            {
                try { _settings.Save(originalSettings); }
                catch { rollbackIncomplete = true; }
            }
            throw new InvalidOperationException(
                rollbackIncomplete
                    ? "完整 AI 配置未导入，且自动回滚未完全完成；请先关闭应用并检查当前 AI 配置与 Windows 凭据管理器。"
                    : "完整 AI 配置未导入；原配置保持不变。",
                ex);
        }

        var cleanupIncomplete = false;
        foreach (var target in oldCredentialTargets)
        {
            try { _secretStore.Delete(target); }
            catch { cleanupIncomplete = true; }
        }

        _customProviders = document.CustomProviders.ToArray();
        OnPCFor(nameof(Providers));
        OnPCFor(nameof(AiConfigurationTemplates));
        ReloadAi();
        Status = cleanupIncomplete
            ? $"已导入并启用“{preview.ActiveConfigurationName}”；旧凭据清理未完全完成。"
            : $"已导入并启用“{preview.ActiveConfigurationName}”，共 {preview.ConfigurationCount} 组配置。";
        return new CompleteAiImportResult(
            preview.ConfigurationCount,
            preview.ConfigurationWithKeyCount,
            preview.CustomProviderCount,
            preview.ActiveConfigurationName,
            cleanupIncomplete);
    }

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
        AiProviderPresetStore.Save(Path.Combine(_settings!.AppDataDir,"provider-presets.json"),presets);
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
