using AiPet.AI;
using AiPet.Todos;
using AiPet.ToolWindow;
using System.IO;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class TodoViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aipet-todo-vm-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now =
        new(2026, 8, 28, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Ai_create_has_no_side_effect_before_confirmation_and_can_be_undone()
    {
        var store = new TodoStore(_root, () => _now);
        var ai = new FakeTodoAiClient(AiTodoParseResult.DraftReady(new AiTodoDraft(
            AiTodoOperation.Create,
            "提交周报",
            null,
            "附数据",
            _now.AddDays(1).AddHours(8),
            _now.AddDays(1).AddHours(6),
            false,
            false,
            null)));
        var vm = CreateViewModel(store, ai);
        vm.AiInput = "明天下午 3 点提醒我提交周报";

        await vm.ParseAiAsync();

        Assert.True(vm.HasAiDraft);
        Assert.Empty(store.Load());
        Assert.True(vm.ConfirmAiCommand.CanExecute(null));

        vm.ConfirmAiCommand.Execute(null);
        Assert.Equal("提交周报", Assert.Single(store.Load()).Title);
        Assert.True(vm.CanUndoAiAction);

        vm.UndoAiCommand.Execute(null);
        Assert.Empty(store.Load());
    }

    [Fact]
    public async Task Ai_create_with_only_a_reminder_time_creates_a_reminder_item()
    {
        var store = new TodoStore(_root, () => _now);
        var ai = new FakeTodoAiClient(AiTodoParseResult.DraftReady(new AiTodoDraft(
            AiTodoOperation.Create,
            "起来活动",
            null,
            null,
            null,
            _now.AddHours(1),
            false,
            false,
            null)));
        var vm = CreateViewModel(store, ai);
        vm.AiInput = "一小时后提醒我起来活动";

        await vm.ParseAiAsync();

        Assert.Equal("创建提醒项", vm.AiOperationText);
        vm.ConfirmAiCommand.Execute(null);
        var created = Assert.Single(store.Load());
        Assert.True(created.IsReminder);
        Assert.True(created.ReminderBubbleEnabled);
        Assert.False(created.ReminderRoamEnabled);
    }

    [Fact]
    public async Task Ai_update_requires_unique_selection_for_same_name_items()
    {
        var store = new TodoStore(_root, () => _now);
        var first = store.Create(new TodoItem { Title = "周报", DueAt = _now.AddDays(1) });
        var second = store.Create(new TodoItem { Title = "周报", DueAt = _now.AddDays(2) });
        var ai = new FakeTodoAiClient(AiTodoParseResult.DraftReady(new AiTodoDraft(
            AiTodoOperation.Update,
            null,
            "周报",
            "更新后的备注",
            null,
            null,
            false,
            false,
            null)));
        var vm = CreateViewModel(store, ai);
        vm.AiInput = "把周报备注改一下";

        await vm.ParseAiAsync();

        Assert.True(vm.NeedsAiTarget);
        Assert.False(vm.ConfirmAiCommand.CanExecute(null));
        Assert.Equal(2, vm.AiTargetChoices.Count);
        vm.SelectedAiTarget = vm.AiTargetChoices.Single(row => row.Id == second.Id);
        Assert.True(vm.ConfirmAiCommand.CanExecute(null));

        vm.ConfirmAiCommand.Execute(null);
        var items = store.Load();
        Assert.Equal(string.Empty, items.Single(item => item.Id == first.Id).Notes);
        Assert.Equal("更新后的备注", items.Single(item => item.Id == second.Id).Notes);
    }

    [Fact]
    public async Task Ai_failure_preserves_input_and_manual_creation_remains_available()
    {
        var store = new TodoStore(_root, () => _now);
        var ai = new FakeTodoAiClient(AiTodoParseResult.Failed(
            AiErrorCategory.NetworkUnreachable,
            "无法连接 AI 服务。",
            "请检查网络。"));
        var vm = CreateViewModel(store, ai);
        vm.AiInput = "明天提醒我交作业";

        await vm.ParseAiAsync();

        Assert.Equal("明天提醒我交作业", vm.AiInput);
        Assert.True(vm.HasAiError);
        Assert.Empty(store.Load());

        vm.NewTodoCommand.Execute(null);
        vm.EditorTitle = "手动交作业";
        vm.SaveEditorCommand.Execute(null);
        Assert.Equal("手动交作业", Assert.Single(store.Load()).Title);
    }

    [Fact]
    public void Manual_complete_restore_cancel_and_delete_have_distinct_effects()
    {
        var store = new TodoStore(_root, () => _now);
        var item = store.Create(new TodoItem
        {
            Title = "准备演示",
            ReminderAt = _now.AddHours(2),
        });
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));

        vm.CancelReminderCommand.Execute(item.Id);
        var reminderCancelled = Assert.Single(store.Load());
        Assert.Equal(TodoStatus.Pending, reminderCancelled.Status);
        Assert.Null(reminderCancelled.ReminderAt);

        vm.CompleteTodoCommand.Execute(item.Id);
        Assert.Equal(TodoStatus.Completed, Assert.Single(store.Load()).Status);
        vm.RestoreTodoCommand.Execute(item.Id);
        Assert.Equal(TodoStatus.Pending, Assert.Single(store.Load()).Status);
        Assert.True(vm.DeleteTodo(item.Id));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Manual_reminder_item_requires_and_persists_independent_pet_channels()
    {
        var store = new TodoStore(_root, () => _now);
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));

        vm.NewTodoCommand.Execute(null);
        vm.EditorTitle = "伸展提醒";
        vm.EditorIsReminder = true;
        vm.EditorReminderDate = _now.LocalDateTime.Date;
        vm.EditorReminderTime = "10:00";
        vm.EditorReminderRoamEnabled = true;
        vm.EditorReminderBubbleEnabled = false;
        vm.SaveEditorCommand.Execute(null);

        var saved = Assert.Single(store.Load());
        Assert.True(saved.IsReminder);
        Assert.True(saved.ReminderRoamEnabled);
        Assert.False(saved.ReminderBubbleEnabled);
        Assert.Equal(TodoStatus.Pending, saved.Status);
        Assert.True(vm.CancelReminderCommand.CanExecute(saved.Id));

        vm.CancelReminderCommand.Execute(saved.Id);
        var cancelled = Assert.Single(store.Load());
        Assert.Equal(ReminderState.Cancelled, cancelled.ReminderState);
        Assert.Equal(TodoStatus.Completed, cancelled.Status);
        Assert.Null(cancelled.ReminderAt);
        Assert.Empty(cancelled.AdditionalReminderTimes);

        store.CompleteReminder(saved.Id, _now.AddHours(1));
        vm.RefreshItems();
        Assert.False(vm.RestoreTodoCommand.CanExecute(saved.Id));
    }

    private TodoViewModel CreateViewModel(TodoStore store, ITodoAiClient ai) =>
        new(store, ai, () => new TodoAiConnection(
            "https://example.test",
            "model",
            "test-api-key"), () => _now);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeTodoAiClient(AiTodoParseResult result) : ITodoAiClient
    {
        public Task<AiTodoParseResult> ParseAsync(
            string endpoint,
            string model,
            string apiKey,
            AiTodoParseRequest request,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
