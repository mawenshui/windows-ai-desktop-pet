using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
    public void Local_plan_uses_exact_quarter_boundary_and_skips_existing_blocks()
    {
        var now = new DateTimeOffset(2026, 9, 9, 9, 2, 37, 123, TimeSpan.FromHours(8)).AddTicks(4567);
        var item = new TodayPlanItemInput(Guid.NewGuid(), "focus", string.Empty, null, now);
        var occupied = new TodayPlanOccupiedBlock(
            new DateTimeOffset(2026, 9, 9, 9, 15, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 9, 9, 9, 45, 0, TimeSpan.FromHours(8)));

        var result = OpenAiCompatibleTodoClient.CreateLocalTodayPlan(
            new TodayPlanRequest([item], now, "China Standard Time", OccupiedBlocks: [occupied]));

        var block = Assert.Single(result.Blocks);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 9, 45, 0, TimeSpan.FromHours(8)), block.StartAt);
        Assert.Equal(0, block.StartAt.Ticks % TimeSpan.TicksPerMinute);
    }

    [Fact]
    public void Structured_plan_rejects_a_block_that_overlaps_existing_schedule()
    {
        var item = Input("focus");
        var occupied = new TodayPlanOccupiedBlock(_now.AddHours(1), _now.AddHours(2));
        var json = $$"""
        {"blocks":[
          {"id":"{{item.Id}}","startAt":"2026-09-09T10:30:00+08:00","endAt":"2026-09-09T11:30:00+08:00","reason":"overlap"}
        ]}
        """;

        var result = OpenAiCompatibleTodoClient.ParseTodayPlanJson(
            json,
            new TodayPlanRequest([item], _now, "China Standard Time", OccupiedBlocks: [occupied]));

        Assert.Equal(TodayPlanStatus.Failed, result.Status);
        Assert.Contains("既有安排重叠", result.ErrorMessage);
    }

    [Fact]
    public async Task Ai_payload_sends_unselected_schedule_only_as_anonymous_time_range()
    {
        var item = Input("selected-private-title");
        var occupied = new TodayPlanOccupiedBlock(_now.AddHours(2), _now.AddHours(3));
        var planJson = $$"""
        {"blocks":[{"id":"{{item.Id}}","startAt":"2026-09-09T10:00:00+08:00","endAt":"2026-09-09T10:30:00+08:00","reason":"safe"}]}
        """;
        var response = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = planJson } } },
        });
        var handler = new CapturingHandler(response);
        var client = new OpenAiCompatibleTodoClient(new HttpClient(handler));

        var result = await client.PlanTodayAsync(
            "https://example.test/v1",
            "fixture",
            "fixture-secret",
            new TodayPlanRequest([item], _now, "China Standard Time", OccupiedBlocks: [occupied]),
            CancellationToken.None);

        Assert.Equal(TodayPlanStatus.DraftReady, result.Status);
        using var outer = JsonDocument.Parse(handler.Body!);
        var content = outer.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        using var userPayload = JsonDocument.Parse(content!);
        var busy = Assert.Single(userPayload.RootElement.GetProperty("occupiedBlocks").EnumerateArray());
        Assert.Equal(2, busy.EnumerateObject().Count());
        Assert.True(busy.TryGetProperty("startAt", out _));
        Assert.True(busy.TryGetProperty("endAt", out _));
        Assert.DoesNotContain("unselected-private", content, StringComparison.Ordinal);
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
    public void Stale_undo_uses_recovery_specific_message_and_restores_nothing()
    {
        var clock = _now;
        var store = new TodoStore(_root, () => clock);
        var before = new[]
        {
            store.Create(new TodoItem { Title = "first" }),
            store.Create(new TodoItem { Title = "second" }),
        };
        clock = clock.AddSeconds(1);
        var updated = store.UpdateBatch(before.Select((item, index) => new TodoBatchUpdate(item with
        {
            PlannedStartAt = _now.AddHours(index + 1),
            DueAt = _now.AddHours(index + 2),
        }, item.UpdatedAt)).ToArray());
        clock = clock.AddSeconds(1);
        store.Update(updated[0] with { Notes = "changed after apply" });

        var exception = Assert.Throws<TodoValidationException>(() => store.RestoreBatch(
            before.Zip(updated, (snapshot, changed) => new TodoBatchUpdate(snapshot, changed.UpdatedAt)).ToArray()));

        Assert.Contains("不能撤销旧安排", exception.Message);
        Assert.NotNull(store.Load().Single(item => item.Id == before[1].Id).PlannedStartAt);
    }

    [Fact]
    public async Task View_model_sends_only_selected_items_then_applies_and_undoes_local_plan()
    {
        // Use an offset that differs from common developer and CI time zones so
        // the view model cannot accidentally re-convert the injected wall clock
        // through TimeZoneInfo.Local.
        var now = new DateTimeOffset(2026, 9, 9, 9, 2, 0, TimeSpan.FromHours(14));
        var store = new TodoStore(_root, () => now);
        var selected = store.Create(new TodoItem { Title = "selected", Notes = "private selected note" });
        store.Create(new TodoItem
        {
            Title = "not-selected",
            Notes = "must stay local",
            PlannedStartAt = now.AddMinutes(13),
            DueAt = now.AddMinutes(43),
        });
        var fake = new CapturingTodayPlanClient();
        var vm = new TodoViewModel(store, fake, Connection, () => now);
        vm.TodayPlanCandidates.Single(item => item.Id == selected.Id).IsSelected = true;

        await vm.GenerateTodayPlanAsync(useAi: true);

        var sent = Assert.Single(fake.LastRequest!.Items);
        Assert.Equal("selected", sent.Title);
        Assert.DoesNotContain(fake.LastRequest.Items, item => item.Title == "not-selected");
        var busy = Assert.Single(fake.LastRequest.OccupiedBlocks!);
        Assert.Equal(now.AddMinutes(13), busy.StartAt);
        Assert.Equal(now.AddMinutes(43), busy.EndAt);
        Assert.True(vm.HasTodayPlanDraft);
        Assert.Equal(now.AddMinutes(43), Assert.Single(vm.TodayPlanDraft).Block.StartAt);
        Assert.Contains("已避让 1 个既有时间段", vm.TodayPlanMessage);

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
    public void Today_plan_can_select_the_first_twelve_candidates_and_clear_them_in_one_action()
    {
        var store = new TodoStore(_root, () => _now);
        for (var index = 0; index < 15; index++)
            store.Create(new TodoItem { Title = $"事项 {index + 1:D2}" });
        var vm = new TodoViewModel(store, new CapturingTodayPlanClient(), Connection, () => _now);

        Assert.Equal(15, vm.TodayPlanCandidates.Count);
        Assert.True(vm.SelectFirstTodayPlanCandidatesCommand.CanExecute(null));
        vm.SelectFirstTodayPlanCandidatesCommand.Execute(null);

        Assert.Equal(12, vm.TodayPlanSelectedCount);
        Assert.All(vm.TodayPlanCandidates.Take(12), candidate => Assert.True(candidate.IsSelected));
        Assert.All(vm.TodayPlanCandidates.Skip(12), candidate => Assert.False(candidate.IsSelected));
        Assert.True(vm.ClearTodayPlanSelectionCommand.CanExecute(null));

        vm.ClearTodayPlanSelectionCommand.Execute(null);
        Assert.Equal(0, vm.TodayPlanSelectedCount);
        Assert.All(vm.TodayPlanCandidates, candidate => Assert.False(candidate.IsSelected));
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

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
