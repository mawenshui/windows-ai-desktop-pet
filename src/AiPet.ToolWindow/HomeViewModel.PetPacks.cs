using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using AiPet.Common;
using AiPet.Pet;

namespace AiPet.ToolWindow;

public sealed partial class HomeViewModel
{
    private PetPackLibrary? _petPackLibrary;
    private PetPackScanResult? _petPackScan;
    private PetPackDescriptor? _pendingPetPackPreview;
    private string? _pendingPetPackSource;
    private ImageSource? _pendingPetPackPreviewImage;
    private string _petLibraryStatus = "仅使用本机角色包；不会联网下载或运行代码。";
    private bool _showRemovePetPackConfirmation;

    public ICommand ConfirmPetPackImportCommand { get; private set; } = null!;
    public ICommand CancelPetPackImportCommand { get; private set; } = null!;
    public ICommand RequestRemovePetPackCommand { get; private set; } = null!;
    public ICommand ConfirmRemovePetPackCommand { get; private set; } = null!;
    public ICommand CancelRemovePetPackCommand { get; private set; } = null!;

    public string PetLibraryStatus
    {
        get => _petLibraryStatus;
        private set { if (_petLibraryStatus == value) return; _petLibraryStatus = value; OnPC(); }
    }

    public bool HasPendingPetPackPreview => _pendingPetPackPreview is not null;
    public ImageSource? PendingPetPackPreviewImage => _pendingPetPackPreviewImage;
    public string PendingPetPackTitle => _pendingPetPackPreview?.DisplayName ?? string.Empty;
    public string PendingPetPackDetails => _pendingPetPackPreview is not { } pack
        ? string.Empty
        : $"{pack.Author} · {pack.License} · {pack.Characters.Count} 个角色 · {pack.Frames} 帧 · 解码 {FormatBytes(pack.DecodedBytes)}";
    public string PendingPetPackFingerprint => _pendingPetPackPreview is not { } pack
        ? string.Empty
        : $"内容指纹：{pack.Fingerprint[..Math.Min(12, pack.Fingerprint.Length)]}…";

    private PetCharacterOption? SelectedPetOption =>
        PetCharacters.FirstOrDefault(option => string.Equals(option.Id, _selectedCharacter, StringComparison.Ordinal));

    public string SelectedPetSourceSummary => SelectedPetOption is { } option
        ? $"来源：{option.PackageDisplayName} · {option.LicenseText}"
        : "当前角色来源不可用";
    public bool SelectedPetCanRemove => SelectedPetOption is { IsBuiltIn: false };
    public bool ShowRemovePetPackConfirmation
    {
        get => _showRemovePetPackConfirmation;
        private set { if (_showRemovePetPackConfirmation == value) return; _showRemovePetPackConfirmation = value; OnPC(); }
    }
    public string RemovePetPackPrompt => SelectedPetOption is { } option
        ? $"移除“{option.PackageDisplayName}”？当前角色会先切回内置角色，源文件夹不会被删除。"
        : "移除当前本地角色包？";

    public void ConfigurePetLibrary(
        PetPackLibrary library,
        PetPackScanResult scan,
        string selectedPackageId,
        string selectedCharacter,
        bool selectionFellBack = false)
    {
        _petPackLibrary = library ?? throw new ArgumentNullException(nameof(library));
        RebuildPetCatalog(scan, selectedPackageId, selectedCharacter);
        if (selectionFellBack)
            PetLibraryStatus = "上次选择的角色包不可用，已安全切回内置角色。";
        else if (scan.InvalidPackageCount > 0)
            PetLibraryStatus = $"已加载 {scan.Packages.Count} 个角色包；另有 {scan.InvalidPackageCount} 个无效本地目录已隔离。";
        else
            PetLibraryStatus = $"已加载 {scan.Packages.Count} 个本机角色包；不会联网下载或运行代码。";
    }

    public void PreviewPetPackImport(string sourceDirectory)
    {
        if (_petPackLibrary is null)
        {
            PetLibraryStatus = "角色库尚未初始化，请重启应用后重试。";
            return;
        }

        try
        {
            var preview = _petPackLibrary.PreviewImport(sourceDirectory);
            var image = new PetFrameCache(preview.RootPath, preview.Manifest, preview.PreferredCharacter)
                .GetFrame("idle", Direction8.Down, 0);
            if (image is null) throw new PetPackException(PetPackError.InvalidImage, "角色包预览无法读取。");
            _pendingPetPackPreview = preview;
            _pendingPetPackSource = sourceDirectory;
            _pendingPetPackPreviewImage = image;
            RaisePendingPetPackStateChanged();
            PetLibraryStatus = "安全检查通过；确认后才会复制到本机角色库。";
        }
        catch (Exception ex) when (ex is PetPackException or IOException or UnauthorizedAccessException)
        {
            ClearPendingPetPackPreview();
            PetLibraryStatus = DescribePetPackFailure(ex);
        }
    }

    public bool HandlePetPackEscape()
    {
        if (HasPendingPetPackPreview)
        {
            ClearPendingPetPackPreview();
            PetLibraryStatus = "已取消角色包导入。";
            return true;
        }
        if (ShowRemovePetPackConfirmation)
        {
            ShowRemovePetPackConfirmation = false;
            return true;
        }
        return false;
    }

    internal bool TryGetSelectedPetLicensePath(out string path, out string displayName)
    {
        path = string.Empty;
        displayName = string.Empty;
        if (_petPackLibrary is null || SelectedPetOption is not { } option) return false;
        try
        {
            var package = _petPackLibrary.Resolve(option.PackageId);
            path = package.LicensePath;
            displayName = package.DisplayName;
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private void InitializePetPackCommands()
    {
        ConfirmPetPackImportCommand = new RelayCommand(_ => ConfirmPetPackImport(), _ => HasPendingPetPackPreview);
        CancelPetPackImportCommand = new RelayCommand(_ =>
        {
            ClearPendingPetPackPreview();
            PetLibraryStatus = "已取消角色包导入。";
        }, _ => HasPendingPetPackPreview);
        RequestRemovePetPackCommand = new RelayCommand(_ =>
        {
            ShowRemovePetPackConfirmation = true;
            OnPCFor(nameof(RemovePetPackPrompt));
        }, _ => SelectedPetCanRemove);
        ConfirmRemovePetPackCommand = new RelayCommand(_ => ConfirmRemovePetPack(), _ => SelectedPetCanRemove && ShowRemovePetPackConfirmation);
        CancelRemovePetPackCommand = new RelayCommand(_ => ShowRemovePetPackConfirmation = false, _ => ShowRemovePetPackConfirmation);
    }

    private void ApplyPetSelection(PetCharacterOption option)
    {
        var selection = new PetCharacterSelection(option.PackageId, option.CharacterId, ResolvePackageRoot(option.PackageId));
        if (PetSelectionChanged is not null)
            PetSelectionChanged.Invoke(selection);
        else
            CharacterChanged?.Invoke(option.CharacterId);
    }

    private string ResolvePackageRoot(string packageId)
    {
        if (_petPackLibrary is null) return string.Empty;
        return _petPackLibrary.Resolve(packageId).RootPath;
    }

    private void ConfirmPetPackImport()
    {
        if (_petPackLibrary is null || _pendingPetPackPreview is null || string.IsNullOrWhiteSpace(_pendingPetPackSource)) return;
        try
        {
            var imported = _petPackLibrary.Import(_pendingPetPackSource, _pendingPetPackPreview);
            var current = SelectedPetOption;
            var scan = _petPackLibrary.Scan();
            RebuildPetCatalog(
                scan,
                current?.PackageId ?? PetPackValidator.BuiltInPackageId,
                current?.CharacterId ?? "hero");
            ClearPendingPetPackPreview();
            PetLibraryStatus = $"“{imported.DisplayName}”已导入；选择角色卡即可应用。";
        }
        catch (Exception ex) when (ex is PetPackException or IOException or UnauthorizedAccessException)
        {
            ClearPendingPetPackPreview();
            PetLibraryStatus = DescribePetPackFailure(ex);
        }
    }

    private void ConfirmRemovePetPack()
    {
        if (_petPackLibrary is null || SelectedPetOption is not { IsBuiltIn: false } selected) return;
        var packageId = selected.PackageId;
        try
        {
            var builtIn = _petPackScan?.Packages.FirstOrDefault(package => package.IsBuiltIn)
                ?? _petPackLibrary.GetBuiltIn();
            var fallback = PetCharacters.First(option =>
                option.PackageId == builtIn.Id && option.CharacterId == builtIn.PreferredCharacter);
            SelectedCharacter = fallback.Id;
            if (!string.Equals(_selectedCharacter, fallback.Id, StringComparison.Ordinal))
                throw new IOException("切换内置角色失败。");
            _petPackLibrary.Remove(packageId);
            RebuildPetCatalog(_petPackLibrary.Scan(), builtIn.Id, builtIn.PreferredCharacter);
            ShowRemovePetPackConfirmation = false;
            PetLibraryStatus = "本地角色包已移除；导入时选择的源文件夹未被修改。";
        }
        catch (Exception ex) when (ex is PetPackException or IOException or UnauthorizedAccessException)
        {
            ShowRemovePetPackConfirmation = false;
            PetLibraryStatus = DescribePetPackFailure(ex);
        }
    }

    private void RebuildPetCatalog(PetPackScanResult scan, string selectedPackageId, string selectedCharacter)
    {
        _petPackScan = scan;
        _petCharacterPreviewsConfigured = false;
        PetCharacters.Clear();
        foreach (var package in scan.Packages)
        {
            foreach (var character in package.Characters)
            {
                var option = new PetCharacterOption(
                    SelectionKey(package.Id, character),
                    package.Id,
                    character,
                    CharacterDisplayName(package, character),
                    package.DisplayName,
                    $"{package.Author} · {package.License}",
                    package.IsBuiltIn);
                try
                {
                    option.Preview = new PetFrameCache(package.RootPath, package.Manifest, character)
                        .GetFrame("idle", Direction8.Down, 0);
                }
                catch
                {
                    option.Preview = null;
                }
                PetCharacters.Add(option);
            }
        }
        _petCharacterPreviewsConfigured = true;

        var selected = PetCharacters.FirstOrDefault(option =>
            option.PackageId == selectedPackageId && option.CharacterId == selectedCharacter);
        selected ??= PetCharacters.FirstOrDefault(option => option.IsBuiltIn)
            ?? throw new InvalidDataException("内置角色包没有可用角色。");
        _selectedCharacter = selected.Id;
        OnPCFor(nameof(SelectedCharacter));
        RaisePetSelectionStateChanged();
    }

    private void RaisePetSelectionStateChanged()
    {
        ShowRemovePetPackConfirmation = false;
        OnPCFor(nameof(SelectedPetSourceSummary));
        OnPCFor(nameof(SelectedPetCanRemove));
        OnPCFor(nameof(RemovePetPackPrompt));
        (RequestRemovePetPackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ConfirmRemovePetPackCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaisePendingPetPackStateChanged()
    {
        OnPCFor(nameof(HasPendingPetPackPreview));
        OnPCFor(nameof(PendingPetPackPreviewImage));
        OnPCFor(nameof(PendingPetPackTitle));
        OnPCFor(nameof(PendingPetPackDetails));
        OnPCFor(nameof(PendingPetPackFingerprint));
        (ConfirmPetPackImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelPetPackImportCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ClearPendingPetPackPreview()
    {
        _pendingPetPackPreview = null;
        _pendingPetPackSource = null;
        _pendingPetPackPreviewImage = null;
        RaisePendingPetPackStateChanged();
    }

    private static string CharacterDisplayName(PetPackDescriptor package, string character)
    {
        if (package.IsBuiltIn)
        {
            return character switch
            {
                "hero" => "红帽勇者",
                "base" => "方块伙伴",
                "skeleton" => "骷髅小队长",
                "monster" => "森林怪兽",
                _ => character,
            };
        }
        return character.Replace('_', ' ').Replace('-', ' ');
    }

    private static string SelectionKey(string packageId, string character) => $"{packageId}::{character}";

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:F1} MiB"
        : $"{Math.Max(1, bytes / 1024d):F0} KiB";

    private static string DescribePetPackFailure(Exception exception)
    {
        if (exception is not PetPackException pet) return "角色包操作未完成；请检查文件夹权限后重试。";
        return pet.Error switch
        {
            PetPackError.DuplicateId => "同 ID 的角色包已经存在；请先移除旧包或使用不同 ID。",
            PetPackError.UnsupportedLicense => "角色包许可不在支持列表中，未导入。",
            PetPackError.ResourceBudgetExceeded => "角色包超出帧数、图片尺寸或容量上限，未导入。",
            PetPackError.PreviewChanged => "角色包在预览后发生变化；请重新选择并确认。",
            PetPackError.UnsafePath => "角色包包含不安全路径或链接，已拒绝。",
            PetPackError.InvalidImage => "角色包包含无法解码的 PNG 图片，已拒绝。",
            PetPackError.BuiltInProtected => "内置角色包受保护，不能移除。",
            _ => "角色包格式或来源证明不符合安全规则，未导入。",
        };
    }
}
