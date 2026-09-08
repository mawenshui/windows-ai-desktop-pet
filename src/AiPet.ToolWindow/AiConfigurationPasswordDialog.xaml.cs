using System.Windows;

namespace AiPet.ToolWindow;

public partial class AiConfigurationPasswordDialog : Window
{
    private readonly bool _requiresConfirmation;

    public AiConfigurationPasswordDialog(bool requiresConfirmation)
    {
        InitializeComponent();
        _requiresConfirmation = requiresConfirmation;
        ConfirmationPanel.Visibility = requiresConfirmation ? Visibility.Visible : Visibility.Collapsed;
        PurposeTextBlock.Text = requiresConfirmation
            ? "设置导出文件的迁移口令。导入时必须输入相同口令。"
            : "输入导出时设置的迁移口令，解密后将先显示导入摘要。";
        ConfirmButton.Content = requiresConfirmation ? "加密导出" : "解密预览";
        Loaded += (_, _) => PasswordInput.Focus();
        UpdateState();
    }

    public string Password { get; private set; } = string.Empty;

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        var longEnough = PasswordInput.Password.Length >= AiPet.AI.EncryptedAiConfigurationBundle.MinimumPasswordLength;
        var matches = !_requiresConfirmation || PasswordInput.Password == PasswordConfirmationInput.Password;
        ConfirmButton.IsEnabled = longEnough && matches;
        ErrorTextBlock.Text = PasswordInput.Password.Length == 0
            ? string.Empty
            : !longEnough
                ? $"口令至少需要 {AiPet.AI.EncryptedAiConfigurationBundle.MinimumPasswordLength} 个字符。"
                : !matches
                    ? "两次输入的口令不一致。"
                    : string.Empty;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateState();
        if (!ConfirmButton.IsEnabled) return;
        Password = PasswordInput.Password;
        DialogResult = true;
    }
}
