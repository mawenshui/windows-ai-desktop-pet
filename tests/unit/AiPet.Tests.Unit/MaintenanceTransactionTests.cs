using System.IO;
using System.IO.Compression;
using AiPet.Storage;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class MaintenanceTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),"aipet-maintenance-" + Guid.NewGuid().ToString("N"));
    public MaintenanceTransactionTests() => Directory.CreateDirectory(_root);
    private string Prepare()
    {
        File.WriteAllText(Path.Combine(_root,"settings.json"), "{\"schemaVersion\":3,\"pet\":{\"preferredCharacter\":\"hero\"}}");
        File.WriteAllText(Path.Combine(_root,"todos.json"), "{\"schemaVersion\":2,\"items\":[]}");
        return new DataMaintenanceService(_root).Backup(Path.Combine(_root,"test.zip"), DataModule.Settings | DataModule.Todos);
    }
    [Theory]
    [InlineData("../outside.json")]
    [InlineData("icons/../../outside.png")]
    [InlineData("icons/x.png:stream")]
    [InlineData("credentials.json")]
    public void Malicious_archive_never_changes_live_data(string entryName)
    {
        var zip = Prepare();
        using (var archive = ZipFile.Open(zip,ZipArchiveMode.Update))
        using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open())) writer.Write("evil");
        var original = File.ReadAllText(Path.Combine(_root,"settings.json"));
        Assert.Throws<InvalidDataException>(() => new DataMaintenanceService(_root).Restore(zip,DataModule.AllNonSecret));
        Assert.Equal(original,File.ReadAllText(Path.Combine(_root,"settings.json")));
    }
    [Fact]
    public void Highly_compressed_oversized_entry_is_rejected_before_live_files_change()
    {
        var zip = Prepare();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        using (var entry = archive.CreateEntry("logs/oversized.log", CompressionLevel.SmallestSize).Open())
        {
            var block = new byte[1024 * 1024];
            for (var index = 0; index < 65; index++) entry.Write(block);
        }
        Assert.True(new FileInfo(zip).Length < 1024 * 1024);
        var original = File.ReadAllText(Path.Combine(_root, "todos.json"));
        Assert.Throws<InvalidDataException>(() => new DataMaintenanceService(_root).QueueRestore(zip, DataModule.AllNonSecret));
        Assert.Equal(original, File.ReadAllText(Path.Combine(_root, "todos.json")));
        Assert.False(File.Exists(Path.Combine(_root, ".maintenance", "pending.json")));
    }
    [Fact]
    public void Invalid_schema_and_changed_hash_are_rejected_before_queue()
    {
        var zip=Prepare();
        using (var archive=ZipFile.Open(zip,ZipArchiveMode.Update))
        { archive.GetEntry("todos.json")!.Delete(); using var writer=new StreamWriter(archive.CreateEntry("todos.json").Open()); writer.Write("{\"schemaVersion\":99,\"items\":[]}"); }
        Assert.Throws<InvalidDataException>(() => new DataMaintenanceService(_root).QueueRestore(zip,DataModule.Todos));
        Assert.False(File.Exists(Path.Combine(_root,".maintenance","pending.json")));
    }
    [Fact]
    public void Mid_restore_failure_rolls_back_all_modules()
    {
        var zip=Prepare();
        File.WriteAllText(Path.Combine(_root,"settings.json"), "original-settings");
        File.WriteAllText(Path.Combine(_root,"todos.json"), "original-todos");
        var result=new DataMaintenanceService(_root, afterApply: _ => throw new IOException("injected")).Restore(zip,DataModule.Settings | DataModule.Todos);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.Completed);
        Assert.Equal("original-settings",File.ReadAllText(Path.Combine(_root,"settings.json")));
        Assert.Equal("original-todos",File.ReadAllText(Path.Combine(_root,"todos.json")));
    }
    [Fact]
    public void Queued_restore_waits_until_next_start_and_keeps_snapshot()
    {
        var zip=Prepare();
        File.WriteAllText(Path.Combine(_root,"todos.json"), "before-restart");
        var service=new DataMaintenanceService(_root);
        service.QueueRestore(zip,DataModule.Todos);
        Assert.Equal("before-restart",File.ReadAllText(Path.Combine(_root,"todos.json")));
        Assert.Empty(service.ApplyPending().Errors);
        Assert.Contains("schemaVersion", File.ReadAllText(Path.Combine(_root,"todos.json")));
        Assert.Contains(Directory.EnumerateFiles(Path.Combine(_root,".maintenance"),"todos.json",SearchOption.AllDirectories), file => File.ReadAllText(file)=="before-restart");
    }
    [Fact]
    public void Logs_use_their_actual_separate_root_and_reset_clears_sqlite_sidecars()
    {
        var logs=Path.Combine(_root,"local","logs"); Directory.CreateDirectory(logs); File.WriteAllText(Path.Combine(logs,"aipet-debug.log"),"stable-code");
        foreach (var suffix in new[] { "","-wal","-shm" }) File.WriteAllText(Path.Combine(_root,"index.db" + suffix),"derived");
        var service=new DataMaintenanceService(_root,logs);
        Assert.True(service.Preview().Single(item => item.Module==DataModule.Logs).Bytes>0);
        service.QueueReset(DataModule.SearchIndex | DataModule.Logs);
        Assert.Empty(service.ApplyPending().Errors);
        Assert.False(Directory.Exists(logs));
        Assert.Empty(Directory.EnumerateFiles(_root,"index.db*"));
    }
    [Fact]
    public void Actual_todos_queue_shortcut_icons_and_provider_presets_round_trip_together()
    {
        var clock=DateTimeOffset.Now;
        var store=new AiPet.Todos.TodoStore(_root,()=>clock);
        var item=store.Create(new AiPet.Todos.TodoItem {Title="anonymous",ReminderAt=clock.AddMinutes(1),IsReminder=true});
        var center=new AiPet.Todos.NotificationCenter(_root,()=>clock);
        center.Enqueue(new(item,false)); clock=clock.AddMinutes(2); store.AdvanceReminder(item.Id,clock,true);
        var shortcuts=new AiPet.Shortcuts.ShortcutStore(_root); Directory.CreateDirectory(shortcuts.IconsDir);
        var icon=Path.Combine(shortcuts.IconsDir,"fixture.png"); File.WriteAllBytes(icon,new byte[]{1,2,3});
        shortcuts.Add(new AiPet.Shortcuts.ShortcutItem {DisplayName="fixture",TargetPath=Path.Combine(_root,"referenced.txt"),IconPath=icon});
        File.WriteAllText(Path.Combine(_root,"provider-presets.json"),"{\"schemaVersion\":1,\"providers\":[]}");
        new AiPet.Todos.DailyJournalStore(_root,()=>clock).SaveNote(DateOnly.FromDateTime(clock.LocalDateTime),"anonymous journal",0);
        var archive=new DataMaintenanceService(_root).Backup(Path.Combine(_root,"full.zip"),DataModule.Todos|DataModule.Shortcuts|DataModule.ProviderPresets|DataModule.Journal);
        var target=Path.Combine(_root,"restored"); var result=new DataMaintenanceService(target).Restore(archive,DataModule.Todos|DataModule.Shortcuts|DataModule.ProviderPresets|DataModule.Journal);
        Assert.Empty(result.Errors); Assert.Equal(AiPet.Todos.ReminderState.Queued,Assert.Single(new AiPet.Todos.TodoStore(target).Load()).ReminderState);
        Assert.Single(new AiPet.Todos.NotificationCenter(target).Entries);
        var restoredIcon=Assert.Single(new AiPet.Shortcuts.ShortcutStore(target).Load()).IconPath;
        Assert.Equal(Path.Combine(target,"icons","fixture.png"),restoredIcon); Assert.True(File.Exists(restoredIcon));
        Assert.True(File.Exists(Path.Combine(target,"provider-presets.json")));
        Assert.Equal("anonymous journal",Assert.Single(new AiPet.Todos.DailyJournalStore(target).Load().Entries).Note);
    }
    [Fact]
    public void Interrupted_restore_recovers_the_pre_operation_snapshot()
    {
        var directory=Path.Combine(_root,".maintenance","interrupted"); Directory.CreateDirectory(Path.Combine(directory,"before"));
        File.WriteAllText(Path.Combine(directory,"before","todos.json"),"original");
        File.WriteAllText(Path.Combine(_root,"todos.json"),"partially-written");
        File.WriteAllText(Path.Combine(directory,"journal.json"),"{\"Phase\":\"applying\",\"Snapshots\":[{\"Module\":4,\"Existed\":true}]}");
        new DataMaintenanceService(_root).RecoverInterrupted();
        Assert.Equal("original",File.ReadAllText(Path.Combine(_root,"todos.json")));
        Assert.Contains("rolled-back",File.ReadAllText(Path.Combine(directory,"journal.json")));
    }
    [Theory]
    [InlineData("{\"schemaVersion\":3,\"items\":[null]}")]
    [InlineData("{\"schemaVersion\":99,\"items\":[]}")]
    [InlineData("{not-json")]
    public void Invalid_todo_data_cannot_be_replaced_by_an_empty_load(string broken)
    {
        var path=Path.Combine(_root,"todos.json"); File.WriteAllText(path,broken);
        var store=new AiPet.Todos.TodoStore(_root); Assert.Empty(store.Load());
        Assert.Throws<InvalidDataException>(()=>store.Create(new AiPet.Todos.TodoItem {Title="new"}));
        Assert.Equal(broken,File.ReadAllText(path));
    }
    [Fact]
    public void Uninstall_cleanup_deletes_only_owned_files_and_known_credential_references()
    {
        var settings=new SettingsStore(_root); settings.Save(SettingsStore.Defaults());
        File.WriteAllText(Path.Combine(_root,"unrelated.txt"),"keep");
        var targets=new List<string>();
        var result=new DataMaintenanceService(_root).RemoveApplicationData(targets.Add);
        Assert.Empty(result.Errors); Assert.False(File.Exists(settings.SettingsPath));
        Assert.True(File.Exists(Path.Combine(_root,"unrelated.txt")));
        Assert.Contains("WindowsAiDesktopPet:Updates:GitHub", targets);
        Assert.All(targets,target=>Assert.True(
            target.StartsWith("WindowsAiDesktopPet:AI:", StringComparison.Ordinal)
            || target == "WindowsAiDesktopPet:Updates:GitHub"));
    }
    public void Dispose() => Directory.Delete(_root,true);
}
