using System.IO;
using System.Linq;
using AiPet.AI;
using AiPet.Todos;
using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class TodayPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aipet-today-plan-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 9, 9, 9, 2, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Structured_plan_accepts_exact_non_overlapping_selected_ids()
    {
        var first = Input("first");
        var second = Input("second");
        var request = new TodayPlanRequest([first, second], _now, "China Standard Time");
        var json = $$"""
        {"blocks":[
          {"id":"{{first.Id}}","startAt":"2026-09-09T10:00:00+08:00","endAt":"2026-09-09T10:30:00+08:00","reason":"先完成"},
          {"id":"{{second.Id}}","startAt":"2026-09-09T10:30:00+08:00","endAt":"2026-09-09T11:00:00+08:00","reason":"随后处理"}
        ]}
        """;

        var result = OpenAiCompatibleTodoClient.ParseTodayPlanJson(json, request);

        Assert.Equal(TodayPlanStatus.DraftReady, result.Status);
        Assert.Equal([first.Id, second.Id], result.Blocks.Select(block => block.Id));

        var startsNow = $$"""
        {"blocks":[
          {"id":"{{first.Id}}","startAt":"2026-09-09T09:02:00+08:00","endAt":"2026-09-09T09:32:00+08:00","reason":"not future"}
        ]}
        """;
        var startsNowResult = OpenAiCompatibleTodoClient.ParseTodayPlanJson(
            startsNow,
            new TodayPlanRequest([first], _now, "China Standard Time"));
        Assert.Equal(TodayPlanStatus.Failed, startsNowResult.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Structured_plan_rejects_unselected_or_overlapping_blocks(bool useUnselectedId)
    {
        var first = Input("first");
        var second = Input("second");
        var request = new TodayPlanRequest([first, second], _now, "China Standard Time");
        var secondId = useUnselectedId ? Guid.NewGuid() : second.Id;
        var secondStart = useUnselectedId ? "10:30" : "10:15";
        var json = $$"""
        {"blocks":[
          {"id":"{{first.Id}}","startAt":"2026-09-09T10:00:00+08:00","endAt":"2026-09-09T10:30:00+08:00","reason":"one"},
          {"id":"{{secondId}}","startAt":"2026-09-09T{{secondStart}}:00+08:00","endAt":"2026-09-09T11:00:00+08:00","reason":"two"}
        ]}
        """;

        var result = OpenAiCompatibleTodoClient.ParseTodayPlanJson(json, request);

        Assert.Equal(TodayPlanStatus.Failed, result.Status);
        Assert.Empty(result.Blocks);
    }

    [Fact]
    public void Local_plan_orders_due_items_and_allocates_future_half_hour_blocks()
    {
        var later = Input("later", _now.AddHours(5));
        var sooner = Input("sooner", _now.AddHours(2));
        var result = OpenAiCompatibleTodoClient.CreateLocalTodayPlan(
            new TodayPlanRequest([later, sooner], _now, "China Standard Time"));

        Assert.Equal(TodayPlanStatus.DraftReady, result.Status);
        Assert.Equal(sooner.Id, result.Blocks[0].Id);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 9, 15, 0, TimeSpan.FromHours(8)), result.Blocks[0].StartAt);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Blocks[0].EndAt - result.Blocks[0].StartAt);
        Assert.Equal(result.Blocks[0].EndAt, result.Blocks[1].StartAt);
    }

    [Fact]
    public void Batch_update_is_atomic_on_stale_input_and_can_be_undone_as_one_write()
    {
        var clock = _now;
        var store = new TodoStore(_root, () => clock);
        var first = store.Create(new TodoItem { Title = "first", DueAt = _now.AddHours(3) });
        var second = store.Create(new TodoItem { Title = "second" });
        var staleFirst = first;
        clock = clock.AddSeconds(1);
        first = store.Update(first with { Notes = "changed" });

        Assert.Throws<TodoValidationException>(() => store.UpdateBatch([
            new TodoBatchUpdate(staleFirst with { PlannedStartAt = _now.AddHours(1), DueAt = _now.AddHours(2) }, staleFirst.UpdatedAt),
            new TodoBatchUpdate(second with { PlannedStartAt = _now.AddHours(2), DueAt = _now.AddHours(3) }, second.UpdatedAt),
        ]));
        Assert.Null(store.Load().Single(item => item.Id == second.Id).PlannedStartAt);

        var before = store.Load().Where(item => item.Id == first.Id || item.Id == second.Id).ToArray();
        var changeEvents = 0;
        store.Changed += () => changeEvents++;
        var updated = store.UpdateBatch(before.Select((item, index) => new TodoBatchUpdate(item with
        {
            PlannedStartAt = _now.AddHours(index + 1),
            DueAt = _now.AddHours(index + 2),
        }, item.UpdatedAt)).ToArray());
        Assert.Equal(1, changeEvents);
        Assert.All(store.Load(), item => Assert.NotNull(item.PlannedStartAt));

        store.RestoreBatch(before.Zip(updated, (snapshot, changed) =>
            new TodoBatchUpdate(snapshot, changed.UpdatedAt)).ToArray());
        Assert.Equal(2, changeEvents);
        Assert.All(store.Load(), item => Assert.Null(item.PlannedStartAt));
    }

    [Fact]
    public async Task View_model_sends_only_selected_items_then_applies_and_undoes_local_plan()
    {
        var store = new TodoStore(_root, () => _now);
        var selected = store.Create(new TodoItem { Title = "selected", Notes = "private selected note" });
        store.Create(new TodoItem { Title = "not-selected", Notes = "must stay local" });
        var fake = new CapturingTodayPlanClient();
        var vm = new TodoViewModel(store, fake, Connection, () => _now);
        vm.TodayPlanCandidates.Single(item => item.Id == selected.Id).IsSelected = true;

        await vm.GenerateTodayPlanAsync(useAi: true);

        var sent = Assert.Single(fake.LastRequest!.Items);
        Assert.Equal("selected", sent.Title);
        Assert.DoesNotContain(fake.LastRequest.Items, item => item.Title == "not-selected");
        Assert.True(vm.HasTodayPlanDraft);

        vm.ApplyTodayPlanCommand.Execute(null);
        var applied = store.Load().Single(item => item.Id == selected.Id);
        Assert.NotNull(applied.PlannedStartAt);
        Assert.True(vm.CanUndoTodayPlan);

        vm.UndoTodayPlanCommand.Execute(null);
        Assert.Null(store.Load().Single(item => item.Id == selected.Id).PlannedStartAt);
        Assert.False(vm.CanUndoTodayPlan);
    }

    [Fact]
    public void Filters_expose_overdue_and_unscheduled_pending_items()
    {
        var store = new TodoStore(_root, () => _now);
        store.Create(new TodoItem { Title = "overdue", DueAt = _now.AddMinutes(-1) });
        store.Create(new TodoItem { Title = "unscheduled" });
        var vm = new TodoViewModel(store, new CapturingTodayPlanClient(), Connection, () => _now);

        vm.SelectedFilterId = "overdue";
        Assert.Equal("overdue", Assert.Single(vm.Items).Title);
        vm.SelectedFilterId = "unscheduled";
        Assert.Equal("unscheduled", Assert.Single(vm.Items).Title);
    }

    [Fact]
    public void Schema_three_migrates_to_four_and_keeps_a_recovery_copy()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "todos.json"), "{\"schemaVersion\":3,\"items\":[]}");
        var store = new TodoStore(_root, () => _now);

        Assert.Empty(store.Load());
        Assert.Contains("\"schemaVersion\": 4", File.ReadAllText(store.TodoPath));
        Assert.True(File.Exists(store.TodoPath + ".pre-v4.bak"));
        Assert.Empty(new TodoStore(_root, () => _now).Load());
    }

    [Fact]
    public void Planned_start_without_an_end_is_rejected_before_write()
    {
        var store = new TodoStore(_root, () => _now);

        Assert.Throws<TodoValidationException>(() => store.Create(new TodoItem
        {
            Title = "invalid block",
            PlannedStartAt = _now.AddHours(1),
        }));
        Assert.Empty(store.Load());
    }

    private TodayPlanItemInput Input(string title, DateTimeOffset? dueAt = null) =>
        new(Guid.NewGuid(), title, string.Empty, dueAt, _now);

    private static TodoAiConnection Connection() => new("https://example.test/v1", "fixture", "fixture-secret");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class CapturingTodayPlanClient : ITodoAiClient, ITodayPlanAiClient
    {
        public TodayPlanRequest? LastRequest { get; private set; }

        public Task<AiTodoParseResult> ParseAsync(string endpoint, string model, string apiKey, AiTodoParseRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(AiTodoParseResult.Failed(AiErrorCategory.Unknown, "unused", "unused"));

        public Task<TodayPlanResult> PlanTodayAsync(string endpoint, string model, string apiKey, TodayPlanRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(OpenAiCompatibleTodoClient.CreateLocalTodayPlan(request));
        }
    }
}
