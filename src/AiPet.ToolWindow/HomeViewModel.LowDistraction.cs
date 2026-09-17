using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using AiPet.Storage;

namespace AiPet.ToolWindow;

public sealed partial class HomeViewModel
{
    private bool _hidePetDuringFullscreen;
    private bool _clickThroughPetDuringFocus;
    private bool _savedHidePetDuringFullscreen;
    private bool _savedClickThroughPetDuringFocus;
    private string _lowDistractionStatus = "设置仅在专注会话期间生效。";

    public bool HidePetDuringFullscreen
    {
        get => _hidePetDuringFullscreen;
        set { if (_hidePetDuringFullscreen == value) return; _hidePetDuringFullscreen = value; OnPC(); RaiseLowDistractionSaveState(); }
    }

    public bool ClickThroughPetDuringFocus
    {
        get => _clickThroughPetDuringFocus;
        set { if (_clickThroughPetDuringFocus == value) return; _clickThroughPetDuringFocus = value; OnPC(); RaiseLowDistractionSaveState(); }
    }

    public bool HasUnsavedLowDistractionChanges => _settings is not null
        && (HidePetDuringFullscreen != _savedHidePetDuringFullscreen
            || ClickThroughPetDuringFocus != _savedClickThroughPetDuringFocus);
    public string LowDistractionSaveLabel => HasUnsavedLowDistractionChanges ? "保存低打扰设置（有修改）" : "低打扰设置已保存";
    public string LowDistractionStatus
    {
        get => _lowDistractionStatus;
        private set { if (_lowDistractionStatus == value) return; _lowDistractionStatus = value; OnPC(); }
    }

    public ICommand SaveLowDistractionSettingsCommand { get; private set; } = null!;
    public ICommand OpenPetLicenseCommand { get; private set; } = null!;

    private void InitializeLowDistractionCommands()
    {
        SaveLowDistractionSettingsCommand = new RelayCommand(_ => SaveLowDistractionSettings(), _ => HasUnsavedLowDistractionChanges);
        OpenPetLicenseCommand = new RelayCommand(_ => OpenPetLicense());
    }

    private void LoadLowDistractionSettings(AppSettings settings)
    {
        _hidePetDuringFullscreen = settings.Appearance.HidePetDuringFullscreen;
        _clickThroughPetDuringFocus = settings.Focus.ClickThroughPet;
        _savedHidePetDuringFullscreen = _hidePetDuringFullscreen;
        _savedClickThroughPetDuringFocus = _clickThroughPetDuringFocus;
        OnPCFor(nameof(HidePetDuringFullscreen));
        OnPCFor(nameof(ClickThroughPetDuringFocus));
        RaiseLowDistractionSaveState();
    }

    private void SaveLowDistractionSettings()
    {
        if (_settings is null) return;
        try
        {
            var settings = _settings.Load();
            settings.Appearance.HidePetDuringFullscreen = HidePetDuringFullscreen;
            settings.Focus.ClickThroughPet = ClickThroughPetDuringFocus;
            _settings.Save(settings);
            _savedHidePetDuringFullscreen = HidePetDuringFullscreen;
            _savedClickThroughPetDuringFocus = ClickThroughPetDuringFocus;
            LowDistractionStatus = "低打扰设置已保存并应用。";
            RaiseLowDistractionSaveState();
            LowDistractionSettingsChanged?.Invoke(settings.Appearance, settings.Focus);
        }
        catch
        {
            LowDistractionStatus = "低打扰设置未保存；运行状态没有改变。";
        }
    }

    private void RaiseLowDistractionSaveState()
    {
        OnPCFor(nameof(HasUnsavedLowDistractionChanges));
        OnPCFor(nameof(LowDistractionSaveLabel));
        (SaveLowDistractionSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OpenPetLicense()
    {
        try
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var level = 0; level < 10 && directory is not null; level++, directory = directory.Parent)
            {
                var direct = Path.Combine(directory.FullName, "assets", "pets", "RGS_8Directional", "License.txt");
                if (File.Exists(direct))
                {
                    Process.Start(new ProcessStartInfo(direct) { UseShellExecute = true });
                    Status = "已打开 RGS 本地许可文件";
                    return;
                }
                var packaged = Path.Combine(directory.FullName, "assets", "pets", "rgs-8dir", "License.txt");
                if (File.Exists(packaged))
                {
                    Process.Start(new ProcessStartInfo(packaged) { UseShellExecute = true });
                    Status = "已打开 RGS 本地许可文件";
                    return;
                }
            }
            Status = "未找到本地许可文件：assets/pets/RGS_8Directional/License.txt";
        }
        catch
        {
            Status = "无法打开本地许可文件：assets/pets/RGS_8Directional/License.txt";
        }
    }
}
