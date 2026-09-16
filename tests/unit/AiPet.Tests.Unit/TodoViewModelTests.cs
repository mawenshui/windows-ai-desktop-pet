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
    public void Manual_editor_enables_save_only_for_a_non_blank_title()
    {
        var store = new TodoStore(_root, () => _now);
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));

        Assert.False(vm.CanSaveEditor);
        Assert.False(vm.SaveEditorCommand.CanExecute(null));

        vm.NewTodoCommand.Execute(null);
        Assert.False(vm.SaveEditorCommand.CanExecute(null));
        vm.EditorTitle = "   ";
        Assert.False(vm.CanSaveEditor);
        Assert.False(vm.SaveEditorCommand.CanExecute(null));

        vm.EditorTitle = "整理桌面";
        Assert.True(vm.CanSaveEditor);
        Assert.True(vm.SaveEditorCommand.CanExecute(null));
        vm.SaveEditorCommand.Execute(null);

        Assert.Equal("整理桌面", Assert.Single(store.Load()).Title);
        Assert.False(vm.CanSaveEditor);
        Assert.False(vm.SaveEditorCommand.CanExecute(null));
    }

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

    [Fact]
    public void Dirty_editor_requires_an_inline_discard_choice_before_closing()
    {
        var store = new TodoStore(_root, () => _now);
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));

        vm.NewTodoCommand.Execute(null);
        Assert.False(vm.HasUnsavedEditorChanges);
        vm.EditorTitle = "未保存的待办";
        Assert.True(vm.HasUnsavedEditorChanges);
        Assert.Contains("未保存", vm.EditorStateHint);

        vm.CancelEditorCommand.Execute(null);
        Assert.True(vm.IsEditorOpen);
        Assert.True(vm.ShowDiscardEditorConfirmation);
        Assert.True(vm.KeepEditingCommand.CanExecute(null));
        Assert.True(vm.DiscardEditorCommand.CanExecute(null));

        vm.KeepEditingCommand.Execute(null);
        Assert.True(vm.IsEditorOpen);
        Assert.False(vm.ShowDiscardEditorConfirmation);
        Assert.Equal("未保存的待办", vm.EditorTitle);

        vm.CancelEditorCommand.Execute(null);
        vm.DiscardEditorCommand.Execute(null);
        Assert.False(vm.IsEditorOpen);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Empty_state_action_returns_to_pending_then_opens_the_manual_editor()
    {
        var store = new TodoStore(_root, () => _now);
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));

        vm.SelectedFilterId = "completed";
        Assert.Equal("返回待处理", vm.EmptyActionLabel);
        Assert.Contains("已完成 0 条", vm.FilterSummary);
        Assert.Contains("待处理总计 0 条", vm.FilterSummary);
        vm.EmptyStateCommand.Execute(null);

        Assert.Equal("pending", vm.SelectedFilterId);
        Assert.Equal("新建第一条待办", vm.EmptyActionLabel);
        Assert.False(vm.IsEditorOpen);
        vm.EmptyStateCommand.Execute(null);
        Assert.True(vm.IsEditorOpen);
    }

    [Fact]
    public void Manual_create_edit_complete_and_delete_share_one_recoverable_undo_path()
    {
        var store = new TodoStore(_root, () => _now);
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));

        vm.NewTodoCommand.Execute(null);
        vm.EditorTitle = "新建项";
        vm.SaveEditorCommand.Execute(null);
        var created = Assert.Single(store.Load());
        Assert.True(vm.CanUndoLastAction);
        Assert.Contains("创建待办", vm.UndoLabel);
        vm.UndoLastActionCommand.Execute(null);
        Assert.Empty(store.Load());

        var original = store.Create(new TodoItem { Title = "原标题" });
        vm.RefreshItems();
        vm.EditTodoCommand.Execute(original.Id);
        vm.EditorTitle = "新标题";
        vm.SaveEditorCommand.Execute(null);
        Assert.Equal("新标题", Assert.Single(store.Load()).Title);
        vm.UndoLastActionCommand.Execute(null);
        Assert.Equal("原标题", Assert.Single(store.Load()).Title);

        vm.CompleteTodoCommand.Execute(original.Id);
        Assert.Equal(TodoStatus.Completed, Assert.Single(store.Load()).Status);
        vm.UndoLastActionCommand.Execute(null);
        Assert.Equal(TodoStatus.Pending, Assert.Single(store.Load()).Status);

        vm.DeleteTodoCommand.Execute(original.Id);
        Assert.Empty(store.Load());
        Assert.Contains("删除待办", vm.UndoLabel);
        vm.UndoLastActionCommand.Execute(null);
        Assert.Equal("原标题", Assert.Single(store.Load()).Title);
        Assert.False(vm.CanUndoLastAction);
    }

    [Fact]
    public void Stale_manual_undo_never_overwrites_a_later_change()
    {
        var clock = _now;
        var store = new TodoStore(_root, () => clock);
        var original = store.Create(new TodoItem { Title = "并发保护" });
        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "test-api-key"), () => clock);

        clock = clock.AddSeconds(1);
        vm.CompleteTodoCommand.Execute(original.Id);
        var completed = Assert.Single(store.Load());
        clock = clock.AddSeconds(1);
        store.Update(completed with { Notes = "后续修改" });

        vm.UndoLastActionCommand.Execute(null);

        var current = Assert.Single(store.Load());
        Assert.Equal(TodoStatus.Completed, current.Status);
        Assert.Equal("后续修改", current.Notes);
        Assert.Contains("撤销失败", vm.Status);
        Assert.True(vm.CanUndoLastAction);
    }

    [Fact]
    public void Local_query_filter_counts_and_query_empty_state_share_one_snapshot()
    {
        var clock = _now.AddDays(-1);
        var store = new TodoStore(_root, () => clock);
        store.Create(new TodoItem { Title = "晨会", DueAt = _now.AddHours(-1) });
        store.Create(new TodoItem { Title = "整理桌面", DueAt = _now.AddHours(3) });
        store.Create(new TodoItem { Title = "提交周报", Notes = "include BRIEF notes", DueAt = _now.AddDays(1) });
        store.Create(new TodoItem { Title = "无时间事项" });
        var completed = store.Create(new TodoItem { Title = "已完成事项" });
        store.Complete(completed.Id);
        clock = _now;

        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "key"), () => clock);

        Assert.Equal(4, vm.Filters.Single(filter => filter.Id == "pending").Count);
        Assert.Equal(2, vm.Filters.Single(filter => filter.Id == "today").Count);
        Assert.Equal(1, vm.Filters.Single(filter => filter.Id == "overdue").Count);
        Assert.Equal(1, vm.Filters.Single(filter => filter.Id == "unscheduled").Count);
        Assert.Equal(3, vm.Filters.Single(filter => filter.Id == "upcoming").Count);
        Assert.Equal(1, vm.Filters.Single(filter => filter.Id == "completed").Count);

        vm.TodoListQuery = " brief ";

        Assert.Equal("提交周报", Assert.Single(vm.Items).Title);
        Assert.Contains("匹配 1/4 条", vm.FilterSummary);
        Assert.Equal(4, vm.Filters.Single(filter => filter.Id == "pending").Count);

        vm.TodoListQuery = "没有匹配";
        Assert.Empty(vm.Items);
        Assert.Equal("清空待办查找", vm.EmptyActionLabel);
        Assert.Contains("没有标题或备注匹配", vm.EmptyMessage);
        vm.EmptyStateCommand.Execute(null);

        Assert.False(vm.HasTodoListQuery);
        Assert.Equal(4, vm.Items.Count);
        Assert.Equal("pending", vm.SelectedFilterId);
    }

    [Fact]
    public void Sort_and_reload_restore_only_the_same_visible_selection()
    {
        var clock = _now.AddDays(-1);
        var store = new TodoStore(_root, () => clock);
        var beta = store.Create(new TodoItem { Title = "Beta", DueAt = _now.AddDays(1) });
        clock = clock.AddMinutes(1);
        var alpha = store.Create(new TodoItem { Title = "Alpha", DueAt = _now.AddDays(2) });
        clock = clock.AddMinutes(1);
        var gamma = store.Create(new TodoItem { Title = "Gamma" });
        clock = _now;
        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "key"), () => clock);

        Assert.Equal(new[] { beta.Id, alpha.Id, gamma.Id }, vm.Items.Select(item => item.Id));
        vm.SelectedTodo = vm.Items.Single(item => item.Id == alpha.Id);
        vm.SelectedTodoSort = vm.TodoSortOptions.Single(option => option.Id == "title");

        Assert.Equal(new[] { alpha.Id, beta.Id, gamma.Id }, vm.Items.Select(item => item.Id));
        Assert.Equal(alpha.Id, vm.SelectedTodo?.Id);
        Assert.Contains("已选 1/3", vm.SelectedTodoSummary);

        vm.RefreshItems();
        Assert.Equal(alpha.Id, vm.SelectedTodo?.Id);
        vm.TodoListQuery = "Beta";
        Assert.Null(vm.SelectedTodo);

        vm.ClearTodoListQueryCommand.Execute(null);
        vm.SelectedTodoSortId = "newest";
        Assert.Equal(new[] { gamma.Id, alpha.Id, beta.Id }, vm.Items.Select(item => item.Id));
    }

    [Fact]
    public void Selected_action_commands_follow_item_state_and_keep_undo_semantics()
    {
        var store = new TodoStore(_root, () => _now);
        var created = store.Create(new TodoItem { Title = "聚焦事项", ReminderAt = _now.AddHours(2) });
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));

        vm.SelectedTodo = Assert.Single(vm.Items);
        Assert.Equal("完成", vm.SelectedCompletionLabel);
        Assert.True(vm.ToggleSelectedTodoCompletionCommand.CanExecute(null));
        Assert.True(vm.SnoozeSelectedTodoCommand.CanExecute(null));
        Assert.True(vm.CancelSelectedTodoReminderCommand.CanExecute(null));
        Assert.Contains("聚焦事项", vm.SelectedTodo.AutomationName);

        vm.ToggleSelectedTodoCompletionCommand.Execute(null);
        Assert.Equal(TodoStatus.Completed, Assert.Single(store.Load()).Status);
        Assert.Null(vm.SelectedTodo);

        vm.SelectedFilterId = "completed";
        vm.SelectedTodo = Assert.Single(vm.Items);
        Assert.Equal("恢复", vm.SelectedCompletionLabel);
        vm.ToggleSelectedTodoCompletionCommand.Execute(null);
        Assert.Equal(TodoStatus.Pending, Assert.Single(store.Load()).Status);

        vm.SelectedFilterId = "pending";
        vm.SelectedTodo = Assert.Single(vm.Items);
        vm.DeleteSelectedTodoCommand.Execute(null);
        Assert.Empty(store.Load());
        Assert.True(vm.CanUndoLastAction);
        vm.UndoLastActionCommand.Execute(null);
        Assert.Equal(created.Id, Assert.Single(store.Load()).Id);
    }

    [Fact]
    public void Editor_shortcuts_counts_and_live_validation_match_store_limits()
    {
        var store = new TodoStore(_root, () => _now);
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));
        vm.NewTodoCommand.Execute(null);

        Assert.Contains("填写标题", vm.EditorValidationHint);
        vm.EditorTitle = "写周报";
        vm.EditorNotes = "三行摘要";
        Assert.Equal("3/200", vm.EditorTitleCountText);
        Assert.Equal("4/4000", vm.EditorNotesCountText);
        Assert.True(vm.CanSaveEditor);
        vm.EditorTitle = new string('x', 201);
        Assert.False(vm.CanSaveEditor);
        Assert.Contains("200", vm.EditorValidationHint);
        vm.EditorTitle = "写周报";

        vm.SetDueTodayCommand.Execute(null);
        Assert.Equal(_now.Date, vm.EditorDueDate);
        Assert.Equal("18:00", vm.EditorDueTime);
        vm.EditorIsReminder = true;
        Assert.False(vm.CanSaveEditor);
        Assert.Contains("必须设置提醒", vm.EditorValidationHint);

        vm.SetReminderInThirtyMinutesCommand.Execute(null);
        Assert.Equal(_now.Date, vm.EditorReminderDate);
        Assert.Equal("09:30", vm.EditorReminderTime);
        Assert.True(vm.CanSaveEditor);
        vm.ClearReminderDateCommand.Execute(null);
        Assert.Null(vm.EditorReminderDate);
        Assert.False(vm.CanSaveEditor);
        vm.SetReminderTomorrowCommand.Execute(null);
        Assert.Equal(_now.Date.AddDays(1), vm.EditorReminderDate);
        Assert.Equal("09:00", vm.EditorReminderTime);

        vm.ClearDueCommand.Execute(null);
        vm.EditorDueTime = "25:00";
        Assert.Null(vm.EditorDueDate);
        Assert.False(vm.CanSaveEditor);
        Assert.Contains("HH:mm", vm.EditorValidationHint);
        vm.EditorDueTime = "18:00";
        vm.EditorRecurrence = vm.RecurrenceOptions.Single(option => option.Kind == RecurrenceKind.Daily);
        vm.EditorEndsOn = _now.Date;
        Assert.True(vm.CanSaveEditor);
        vm.SaveEditorCommand.Execute(null);
        Assert.True(vm.HasEditorError);
        vm.EditorEndsOn = _now.Date.AddDays(2);
        Assert.False(vm.HasEditorError);
        Assert.Contains("内容有效", vm.EditorValidationHint);
    }

    [Fact]
    public void Thirty_minute_reminder_shortcut_crosses_midnight_and_urgency_is_not_color_only()
    {
        var late = new DateTimeOffset(2026, 8, 28, 23, 50, 20, TimeSpan.FromHours(8));
        var store = new TodoStore(Path.Combine(_root, "late"), () => late);
        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "key"), () => late);
        vm.NewTodoCommand.Execute(null);
        vm.EditorTitle = "跨午夜提醒";
        vm.EditorIsReminder = true;
        vm.SetReminderInThirtyMinutesCommand.Execute(null);

        Assert.Equal(late.Date.AddDays(1), vm.EditorReminderDate);
        Assert.Equal("00:21", vm.EditorReminderTime);
        Assert.True(vm.CanSaveEditor);

        Assert.Equal("已逾期", new TodoRowViewModel(new TodoItem { DueAt = late.AddMinutes(-1) }, late).UrgencyText);
        Assert.Equal("今天", new TodoRowViewModel(new TodoItem { DueAt = late.AddMinutes(5) }, late).UrgencyText);
        Assert.Equal("未来", new TodoRowViewModel(new TodoItem { DueAt = late.AddDays(2) }, late).UrgencyText);
        Assert.Equal("未安排", new TodoRowViewModel(new TodoItem(), late).UrgencyText);
        Assert.Equal("已完成", new TodoRowViewModel(new TodoItem { Status = TodoStatus.Completed }, late).UrgencyText);
    }

    [Fact]
    public void Notification_projection_search_counts_sort_and_selection_use_one_snapshot()
    {
        var clock = _now;
        var store = new TodoStore(Path.Combine(_root, "notification-projection"), () => clock);
        var center = new NotificationCenter(Path.Combine(_root, "notification-projection"), () => clock);
        var later = store.Create(new TodoItem { Title = "Beta 提醒", ReminderAt = _now.AddHours(2) });
        Assert.True(center.Enqueue(new(later, false)));
        clock = clock.AddMinutes(1);
        var sooner = store.Create(new TodoItem { Title = "Alpha 提醒", ReminderAt = _now.AddHours(1) });
        Assert.True(center.Enqueue(new(sooner, false)));
        clock = clock.AddMinutes(1);
        var handled = store.Create(new TodoItem { Title = "历史提醒", ReminderAt = _now.AddHours(3) });
        Assert.True(center.Enqueue(new(handled, true)));
        center.Handle(center.Entries.Single(entry => entry.TodoId == handled.Id).Id);

        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "key"), () => clock);
        vm.AttachNotificationCenter(center);

        Assert.Equal(2, vm.NotificationFilters.Single(filter => filter.Id == "pending").Count);
        Assert.Equal(1, vm.NotificationFilters.Single(filter => filter.Id == "history").Count);
        Assert.Equal(3, vm.NotificationFilters.Single(filter => filter.Id == "all").Count);
        Assert.Equal(new[] { sooner.Id, later.Id }, vm.NotificationHistory.Select(row => row.TodoId));

        vm.SelectedNotification = vm.NotificationHistory.Single(row => row.TodoId == later.Id);
        vm.RefreshNotifications();
        Assert.Equal(later.Id, vm.SelectedNotification?.TodoId);
        Assert.Contains("已选 2/2", vm.SelectedNotificationSummary);

        vm.NotificationQuery = " beta ";
        Assert.Equal(later.Id, Assert.Single(vm.NotificationHistory).TodoId);
        Assert.Equal(2, vm.NotificationFilters.Single(filter => filter.Id == "pending").Count);
        vm.NotificationQuery = "没有匹配";
        Assert.Empty(vm.NotificationHistory);
        Assert.Null(vm.SelectedNotification);
        Assert.Equal("清空提醒查找", vm.NotificationEmptyActionLabel);
        vm.NotificationEmptyStateCommand.Execute(null);
        Assert.False(vm.HasNotificationQuery);

        vm.SelectedNotificationFilter = vm.NotificationFilters.Single(filter => filter.Id == "history");
        Assert.Equal(handled.Id, Assert.Single(vm.NotificationHistory).TodoId);
        Assert.Equal("已处理", vm.NotificationHistory[0].StateText);
        Assert.Contains("启动后恢复", vm.NotificationHistory[0].AutomationName);

        vm.SelectedNotificationSort = vm.NotificationSortOptions.Single(option => option.Id == "recent");
        vm.SelectedNotificationFilterId = "all";
        Assert.Equal(handled.Id, vm.NotificationHistory[0].TodoId);
    }

    [Fact]
    public void Notification_action_bar_snoozes_target_and_history_cleanup_preserves_todos()
    {
        var root = Path.Combine(_root, "notification-actions");
        var clock = _now;
        var store = new TodoStore(root, () => clock);
        var item = store.Create(new TodoItem { Title = "喝水", ReminderAt = clock.AddMinutes(1) });
        var center = new NotificationCenter(root, () => clock);
        Assert.True(center.Enqueue(new(item, false)));
        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "key"), () => clock);
        vm.AttachNotificationCenter(center);
        vm.SelectedNotification = Assert.Single(vm.NotificationHistory);

        Assert.True(vm.CanActOnSelectedNotification);
        Assert.True(vm.SnoozeSelectedNotificationCommand.CanExecute(null));
        Assert.Contains("喝水", vm.SelectedNotificationSnoozeAutomationName);
        vm.SnoozeMinutes = 30;
        vm.SnoozeSelectedNotificationCommand.Execute(null);

        var snoozed = Assert.Single(store.Load());
        Assert.Equal(ReminderState.Snoozed, snoozed.ReminderState);
        Assert.Equal(clock.AddMinutes(30), snoozed.ReminderAt);
        Assert.Contains("30 分钟后", vm.NotificationStatus);
        Assert.Equal(1, vm.ClearableNotificationCount);

        center.EnqueueChannelTest();
        Assert.Equal(1, center.SubmitNext(_ => true));
        vm.RefreshNotifications();
        Assert.Equal(2, vm.ClearableNotificationCount);
        Assert.True(vm.ClearNotificationHistoryCommand.CanExecute(null));
        vm.ClearNotificationHistoryCommand.Execute(null);

        Assert.Empty(center.Entries);
        Assert.Single(store.Load());
        Assert.Contains("已清除 2 条", vm.NotificationStatus);
        Assert.False(vm.ClearNotificationHistoryCommand.CanExecute(null));
    }

    [Fact]
    public void Quiet_hours_are_validated_as_a_saved_draft_with_presets()
    {
        var root = Path.Combine(_root, "quiet-draft");
        var store = new TodoStore(root, () => _now);
        var center = new NotificationCenter(root, () => _now);
        var vm = CreateViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")));
        vm.AttachNotificationCenter(center);

        Assert.False(vm.HasUnsavedQuietChanges);
        Assert.False(vm.SaveQuietHoursCommand.CanExecute(null));
        Assert.Equal("静默设置已保存", vm.QuietSettingsSaveLabel);

        vm.QuietEnabled = true;
        vm.QuietStart = "invalid";
        Assert.True(vm.HasUnsavedQuietChanges);
        Assert.False(vm.CanSaveQuietSettings);
        Assert.Contains("HH:mm", vm.QuietValidationHint);

        vm.QuietStart = "08:00";
        vm.QuietEnd = "08:00";
        Assert.False(vm.CanSaveQuietSettings);
        Assert.Contains("不能相同", vm.QuietValidationHint);

        vm.ApplyQuietPresetCommand.Execute("night");
        Assert.Equal("22:00", vm.QuietStart);
        Assert.Equal("08:00", vm.QuietEnd);
        Assert.True(vm.CanSaveQuietSettings);
        Assert.Contains("保存后生效", vm.QuietValidationHint);
        vm.SaveQuietHoursCommand.Execute(null);

        Assert.Equal(new QuietHours(true, "22:00", "08:00"), center.Quiet);
        Assert.False(vm.HasUnsavedQuietChanges);
        Assert.False(vm.SaveQuietHoursCommand.CanExecute(null));
        Assert.Contains("22:00–08:00", vm.NotificationStatus);

        vm.ApplyQuietPresetCommand.Execute("lunch");
        Assert.Equal("12:00", vm.QuietStart);
        Assert.Equal("13:00", vm.QuietEnd);
        vm.ApplyQuietPresetCommand.Execute("off");
        Assert.False(vm.QuietEnabled);
        Assert.True(vm.HasUnsavedQuietChanges);
        Assert.Contains("保存后生效", vm.NotificationStatus);
    }

    [Fact]
    public async Task Journal_autosaves_after_debounce_and_projects_today_without_mutating_todos()
    {
        var root = Path.Combine(_root, "journal-autosave");
        var store = new TodoStore(root, () => _now);
        var todo = store.Create(new TodoItem
        {
            Title = "今日任务",
            PlannedStartAt = _now.AddHours(1),
            DueAt = _now.AddHours(2),
        });
        var journal = new DailyJournalStore(root, () => _now);
        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "key"), () => _now, journal);

        vm.SelectedTodoPageMode = vm.TodoPageModes[1];
        vm.JournalNote = "先完成最重要的一件事。";
        await Task.Delay(650);
        await vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("先完成最重要的一件事。", journal.Get(DateOnly.FromDateTime(_now.Date))!.Note);
        Assert.Equal(1, vm.JournalPendingCount);
        Assert.Equal(1, vm.JournalPlannedCount);
        Assert.Equal(todo.Id, Assert.Single(store.Load()).Id);
        Assert.Contains("已保存", vm.JournalSaveStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Journal_date_refresh_finalizes_previous_day_and_selects_today()
    {
        var root = Path.Combine(_root, "journal-rollover");
        var clock = new DateTimeOffset(2026, 9, 14, 23, 59, 0, TimeSpan.FromHours(8));
        var store = new TodoStore(root, () => clock);
        store.Create(new TodoItem { Title = "临睡前完成", Status = TodoStatus.Completed, CompletedAt = clock });
        var journal = new DailyJournalStore(root, () => clock);
        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "key"), () => clock, journal);
        vm.JournalNote = "旧日记录";
        await Task.Delay(650);
        await vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(2));

        clock = clock.AddMinutes(2);
        await vm.RefreshJournalDateAsync();

        var oldEntry = journal.Get(new DateOnly(2026, 9, 14));
        Assert.NotNull(oldEntry!.FinalizedAt);
        Assert.Equal("临睡前完成", Assert.Single(oldEntry.Snapshot).Title);
        Assert.Equal(new DateOnly(2026, 9, 15), vm.SelectedJournalDate);
        Assert.True(vm.IsJournalToday);
    }

    [Fact]
    public async Task Journal_delete_confirmation_removes_only_the_selected_journal()
    {
        var root = Path.Combine(_root, "journal-delete");
        var store = new TodoStore(root, () => _now);
        var todo = store.Create(new TodoItem { Title = "不能被删除" });
        var journal = new DailyJournalStore(root, () => _now);
        var vm = new TodoViewModel(store, new FakeTodoAiClient(AiTodoParseResult.NeedsClarification("unused")),
            () => new TodoAiConnection("https://example.test", "model", "key"), () => _now, journal);
        vm.JournalNote = "准备删除";
        await Task.Delay(650);
        await vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(2));

        vm.RequestDeleteJournalCommand.Execute(null);
        Assert.True(vm.ShowDeleteJournalConfirmation);
        vm.ConfirmDeleteJournalCommand.Execute(null);

        Assert.Null(journal.Get(DateOnly.FromDateTime(_now.Date)));
        Assert.Equal(todo.Id, Assert.Single(store.Load()).Id);
        Assert.Contains("待办未受影响", vm.JournalSaveStatus, StringComparison.Ordinal);
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
