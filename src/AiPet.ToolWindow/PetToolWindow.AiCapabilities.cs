using System.IO;
using System.Windows;
using AiPet.AI;
using Microsoft.Win32;

namespace AiPet.ToolWindow;

public partial class PetToolWindow
{
    private async void VerifyGeneration_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm || !vm.CanVerifyGeneration) return;
        if (MessageBox.Show(this,"向当前服务发送一条固定测试文本，最多生成 256 tokens，可能消耗服务额度。结果只用于验证，不创建待办。", "验证草稿生成",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK) return;
        await vm.VerifyAiGenerationAsync();
    }
    private void CancelAiVerification_Click(object sender,RoutedEventArgs e) { if (DataContext is HomeViewModel vm) vm.CancelAiVerification(); }

    private void ExportCompleteAiConfigurations_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm) return;
        var dialog = new SaveFileDialog
        {
            Filter = "完整 AI 配置包 (*.aipet-ai-config)|*.aipet-ai-config",
            FileName = "windows-ai-desktop-pet-ai-config.aipet-ai-config",
            DefaultExt = AiPet.AI.EncryptedAiConfigurationBundle.FileExtension,
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        var passwordDialog = new AiConfigurationPasswordDialog(requiresConfirmation: true) { Owner = this };
        if (passwordDialog.ShowDialog() != true) return;
        try
        {
            vm.ExportCompleteAiConfigurations(dialog.FileName, passwordDialog.Password);
            MessageBox.Show(
                this,
                "完整 AI 配置已加密导出。文件包含 API Key，请妥善保存文件和迁移口令。",
                "完整 AI 配置",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "完整 AI 配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            MessageBox.Show(this, "导出未完成；请检查文件权限和当前账户的凭据状态。", "完整 AI 配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportCompleteAiConfigurations_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm) return;
        var dialog = new OpenFileDialog
        {
            Filter = "完整 AI 配置包 (*.aipet-ai-config)|*.aipet-ai-config",
            DefaultExt = AiPet.AI.EncryptedAiConfigurationBundle.FileExtension,
        };
        if (dialog.ShowDialog(this) != true) return;
        var passwordDialog = new AiConfigurationPasswordDialog(requiresConfirmation: false) { Owner = this };
        if (passwordDialog.ShowDialog() != true) return;
        try
        {
            var document = vm.ReadCompleteAiConfigurations(dialog.FileName, passwordDialog.Password);
            var preview = EncryptedAiConfigurationBundle.Preview(document);
            var summary = $"来源版本：{preview.SourceVersion}\n导出时间：{preview.ExportedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n配置：{preview.ConfigurationCount} 组（{preview.ConfigurationWithKeyCount} 组含 Key）\n自定义 Provider：{preview.CustomProviderCount} 个\n导入后启用：{preview.ActiveConfigurationName}";
            var decision = MessageBox.Show(
                this,
                $"{summary}\n\n继续将替换当前全部 AI 配置和自定义 Provider 预设，并把 Key 写入当前 Windows 账户的凭据库。其他设置不变。",
                "完整 AI 配置导入预览",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (decision != MessageBoxResult.OK || !TryResolveUnsavedAiChanges("导入完整 AI 配置")) return;
            var result = vm.ImportCompleteAiConfigurations(document);
            MessageBox.Show(
                this,
                result.OldCredentialCleanupIncomplete
                    ? $"已导入 {result.ConfigurationCount} 组配置并启用“{result.ActiveConfigurationName}”。部分旧凭据未能清理，可在 Windows 凭据管理器中复核。"
                    : $"已导入 {result.ConfigurationCount} 组配置并启用“{result.ActiveConfigurationName}”，现在可以直接使用。",
                "完整 AI 配置",
                MessageBoxButton.OK,
                result.OldCredentialCleanupIncomplete ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(this, ex.Message, "完整 AI 配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "完整 AI 配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            MessageBox.Show(this, "完整 AI 配置未导入；原配置保持不变。请检查文件、口令、权限和当前账户的凭据状态。", "完整 AI 配置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportAiConfigurations_Click(object sender,RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm) return;
        var dialog=new SaveFileDialog { Filter="AI 配置 (*.json)|*.json", FileName="aipet-ai-configurations.json" };
        if (dialog.ShowDialog(this)!=true) return;
        try { vm.ExportAiConfigurations(dialog.FileName); }
        catch { MessageBox.Show(this,"导出未完成，请检查文件权限。","AI 配置"); }
    }
    private void ImportAiConfigurations_Click(object sender,RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm) return;
        var dialog=new OpenFileDialog { Filter="AI 配置 (*.json)|*.json" };
        if (dialog.ShowDialog(this)!=true) return;
        try
        {
            var preview=AiConfigurationExchange.Preview(dialog.FileName);
            var summary=string.Join("\n",preview.Take(12).Select(item=>$"{item.Name} · {item.Model}\n{item.Endpoint}"));
            var decision=MessageBox.Show(this,$"将导入 {preview.Count} 组配置（此处最多显示 12 组）：\n{summary}\n\n同名项：是=另存副本，否=跳过。不会切换活动配置或发送请求。", "配置导入预览",MessageBoxButton.YesNoCancel,MessageBoxImage.Information);
            if (decision==MessageBoxResult.Cancel || !TryResolveUnsavedAiChanges("导入配置")) return;
            vm.ImportAiConfigurations(preview,decision==MessageBoxResult.Yes?AiImportConflict.Rename:AiImportConflict.Skip);
        }
        catch { MessageBox.Show(this,"配置未通过校验或无法保存。请使用不含 Key 的配置导出格式。","AI 配置"); }
    }
    private void ImportProviderPresets_Click(object sender,RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm) return;
        var dialog=new OpenFileDialog { Filter="Provider 预设 (*.json)|*.json" };
        if (dialog.ShowDialog(this)!=true) return;
        try
        {
            var preview=AiProviderPresetStore.Load(dialog.FileName);
            if (MessageBox.Show(this,$"导入 {preview.Count} 个预设并替换当前自定义预设列表？已保存配置保持不变。", "预设导入预览",MessageBoxButton.OKCancel)!=MessageBoxResult.OK) return;
            vm.ImportProviderPresets(dialog.FileName);
        }
        catch { MessageBox.Show(this,"预设格式、ID 或端点未通过校验。","AI 预设"); }
    }
}
