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
