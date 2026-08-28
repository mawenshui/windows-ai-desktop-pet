using AiPet.Secrets;

namespace AiPet.ToolWindow;

/// <summary>
/// Isolates Windows Credential Manager access so AI configuration workflows
/// can be verified without writing to a developer's real credential vault.
/// </summary>
public interface IAiSecretStore
{
    string? Load(string targetName);
    void Save(string targetName, string secret);
    void Delete(string targetName);
}

internal sealed class WindowsAiSecretStore : IAiSecretStore
{
    public string? Load(string targetName) => WindowsCredentialStore.Load(targetName);
    public void Save(string targetName, string secret) => WindowsCredentialStore.Save(targetName, secret);
    public void Delete(string targetName) => WindowsCredentialStore.Delete(targetName);
}
