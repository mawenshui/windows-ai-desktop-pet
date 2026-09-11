using System;
using System.IO;
using AiPet.Storage;
using Xunit;

namespace AiPet.Tests.Unit;

public class SettingsStoreTests : IDisposable
{
    private readonly string _root;

    public SettingsStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aipet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Defaults_when_no_file()
    {
        var s = new SettingsStore(_root);
        var loaded = s.Load();
        Assert.Equal("rgs-8dir", loaded.Pet.Id);
        Assert.Equal("hero", loaded.Pet.PreferredCharacter);
        Assert.Equal(440, loaded.ToolWindow.Width);
        Assert.Equal(536, loaded.ToolWindow.Height);
        Assert.False(loaded.ToolWindow.StayOpen);
        Assert.True(loaded.ToolWindow.AlwaysOnTop);
        Assert.Equal(5, loaded.SchemaVersion);
        Assert.True(loaded.Hotkeys.Enabled);
        Assert.Equal("Ctrl+Alt+Space", loaded.Hotkeys.SearchGesture);
        Assert.Equal("Ctrl+Alt+T", loaded.Hotkeys.QuickTodoGesture);
        Assert.True(loaded.Backup.AutomaticEnabled);
        Assert.Equal(7, loaded.Backup.RetentionCount);
        Assert.False(loaded.Updates.PeriodicEnabled);
        Assert.Equal(24, loaded.Updates.IntervalHours);
    }

    [Fact]
    public void Round_trip_preserves_values()
    {
        var s = new SettingsStore(_root);
        var saved = new AppSettings
        {
            Pet = new PetSettings { Id = "rgs-8dir", PreferredCharacter = "monster" },
            ToolWindow = new ToolWindowSettings
            {
                Width = 600,
                Height = 700,
                StayOpen = true,
                AlwaysOnTop = false,
            },
            Search = new SearchSettings
            {
                EnableWildcardSearch = true,
                EnableRegexSearch = true,
                LastScopeId = "apps",
            },
            Hotkeys = new HotkeySettings
            {
                Enabled = false,
                SearchGesture = "Ctrl+Shift+F12",
                QuickTodoGesture = "Win+Alt+T",
            },
            Backup = new BackupSettings { AutomaticEnabled = false, RetentionCount = 12 },
            Updates = new UpdateSettings
            {
                PeriodicEnabled = true,
                IntervalHours = 6,
                AccelerationTemplate = "https://mirror.example.test/{url}",
            },
            Features = new FeatureSettings { EnableContentSearch = true },
        };
        s.Save(saved);
        var loaded = s.Load();
        Assert.Equal("monster", loaded.Pet.PreferredCharacter);
        Assert.Equal(600, loaded.ToolWindow.Width);
        Assert.Equal(700, loaded.ToolWindow.Height);
        Assert.True(loaded.ToolWindow.StayOpen);
        Assert.False(loaded.ToolWindow.AlwaysOnTop);
        Assert.True(loaded.Search.EnableWildcardSearch);
        Assert.True(loaded.Search.EnableRegexSearch);
        Assert.Equal("apps", loaded.Search.LastScopeId);
        Assert.False(loaded.Hotkeys.Enabled);
        Assert.Equal("Ctrl+Shift+F12", loaded.Hotkeys.SearchGesture);
        Assert.Equal("Win+Alt+T", loaded.Hotkeys.QuickTodoGesture);
        Assert.False(loaded.Backup.AutomaticEnabled);
        Assert.Equal(12, loaded.Backup.RetentionCount);
        Assert.True(loaded.Updates.PeriodicEnabled);
        Assert.Equal(6, loaded.Updates.IntervalHours);
        Assert.Equal("https://mirror.example.test/{url}", loaded.Updates.AccelerationTemplate);
        Assert.True(loaded.Features.EnableContentSearch);
    }

    [Fact]
    public void Corrupted_file_falls_back_to_defaults_without_overwriting()
    {
        var s = new SettingsStore(_root);
        Directory.CreateDirectory(s.AppDataDir);
        File.WriteAllText(s.SettingsPath, "{ this is not valid json");

        var loaded = s.Load();
        // Defaults
        Assert.Equal("hero", loaded.Pet.PreferredCharacter);
        // The corrupted file is left on disk (no destructive overwrite).
        Assert.True(File.Exists(s.SettingsPath));
        Assert.Equal("{ this is not valid json", File.ReadAllText(s.SettingsPath));
    }

    [Fact]
    public void Legacy_single_ai_configuration_is_exposed_as_a_selectable_profile()
    {
        var store = new SettingsStore(_root);
        Directory.CreateDirectory(store.AppDataDir);
        File.WriteAllText(store.SettingsPath, """
        {
          "schemaVersion": 1,
          "ai": {
            "providerId": "qwen",
            "endpoint": "https://dashscope.example.test/v1",
            "model": "qwen-plus",
            "secretTargetName": "WindowsAiDesktopPet:AI:qwen",
            "lastStatus": "Connected",
            "lastVerifiedAt": "2026-08-28T10:00:00+00:00"
          }
        }
        """);

        var loaded = store.Load();

        var profile = Assert.Single(loaded.Ai.Profiles);
        Assert.Equal("legacy", profile.Id);
        Assert.Equal("qwen", profile.ProviderId);
        Assert.Equal("qwen-plus", profile.Model);
        Assert.Equal("legacy", loaded.Ai.ActiveProfileId);
        Assert.Equal(5, loaded.SchemaVersion);
        Assert.True(File.Exists(store.SettingsPath + ".pre-v4.bak"));
        Assert.True(File.Exists(store.SettingsPath + ".pre-v5.bak"));
    }

    [Fact]
    public void Schema_v3_upgrades_to_v5_with_new_defaults_and_migration_backups()
    {
        var store = new SettingsStore(_root);
        File.WriteAllText(store.SettingsPath, "{\"schemaVersion\":3,\"pet\":{\"preferredCharacter\":\"base\"}}");

        var loaded = store.Load();

        Assert.Equal(5, loaded.SchemaVersion);
        Assert.Equal("base", loaded.Pet.PreferredCharacter);
        Assert.True(loaded.Hotkeys.Enabled);
        Assert.True(loaded.Backup.AutomaticEnabled);
        Assert.Equal(7, loaded.Backup.RetentionCount);
        Assert.True(File.Exists(store.SettingsPath + ".pre-v4.bak"));
        Assert.True(File.Exists(store.SettingsPath + ".pre-v5.bak"));
        Assert.False(File.Exists(store.SettingsPath + ".pre-v3.bak"));
    }

    [Fact]
    public void Schema_v4_adds_update_defaults_and_preserves_a_pre_v5_backup()
    {
        var store = new SettingsStore(_root);
        File.WriteAllText(store.SettingsPath, "{\"schemaVersion\":4,\"backup\":{\"retentionCount\":9}}");

        var loaded = store.Load();

        Assert.Equal(5, loaded.SchemaVersion);
        Assert.Equal(9, loaded.Backup.RetentionCount);
        Assert.False(loaded.Updates.PeriodicEnabled);
        Assert.Equal(24, loaded.Updates.IntervalHours);
        Assert.True(File.Exists(store.SettingsPath + ".pre-v5.bak"));
        Assert.False(File.Exists(store.SettingsPath + ".pre-v4.bak"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(31, 30)]
    public void Backup_retention_is_normalized_to_supported_bounds(int input, int expected)
    {
        var store = new SettingsStore(_root);
        store.Save(new AppSettings { Backup = new BackupSettings { RetentionCount = input } });

        Assert.Equal(expected, store.Load().Backup.RetentionCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(200, 168)]
    public void Update_interval_is_normalized_to_supported_bounds(int input, int expected)
    {
        var store = new SettingsStore(_root);
        store.Save(new AppSettings { Updates = new UpdateSettings { IntervalHours = input } });

        Assert.Equal(expected, store.Load().Updates.IntervalHours);
    }

    [Fact]
    public void Layout_round_trip()
    {
        var s = new SettingsStore(_root);
        Assert.Null(s.TryLoadLayout());
        s.SaveLayout(new WindowLayout { PetX = 1234, PetY = 567, ToolX = 100, ToolY = 200 });
        var loaded = s.TryLoadLayout();
        Assert.NotNull(loaded);
        Assert.Equal(1234, loaded!.PetX);
        Assert.Equal(567, loaded.PetY);
        Assert.Equal(100, loaded.ToolX);
        Assert.Equal(200, loaded.ToolY);
    }

    [Fact]
    public void Save_recovers_when_legacy_build_created_settings_path_as_directory()
    {
        var store = new SettingsStore(_root);
        Directory.CreateDirectory(store.SettingsPath);
        File.WriteAllText(Path.Combine(store.SettingsPath, "legacy-marker.txt"), "keep");

        store.Save(new AppSettings
        {
            Pet = new PetSettings { PreferredCharacter = "skeleton" },
        });

        Assert.True(File.Exists(store.SettingsPath));
        Assert.Equal("skeleton", store.Load().Pet.PreferredCharacter);
        var backup = Directory.GetDirectories(_root, "settings.json.invalid-directory-backup*");
        Assert.Single(backup);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(backup[0], "legacy-marker.txt")));
    }

    [Fact]
    public void Diagnostic_log_is_kept_in_local_app_data_instead_of_the_install_directory()
    {
        var localAppData = Path.Combine(_root, "local-app-data");

        var path = ApplicationDataPaths.GetDiagnosticLogPath(localAppData);

        Assert.Equal(
            Path.Combine(localAppData, "WindowsAiDesktopPet", "logs", "aipet-debug.log"),
            path);
        Assert.DoesNotContain(AppContext.BaseDirectory, path, StringComparison.OrdinalIgnoreCase);
    }
}
