using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AiPet.AI;

public enum TodayPlanStatus
{
    DraftReady = 0,
    Failed = 1,
}

public sealed record TodayPlanItemInput(
    Guid Id,
    string Title,
    string Notes,
    DateTimeOffset? DueAt,
    DateTimeOffset UpdatedAt);

public sealed record TodayPlanOccupiedBlock(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);

public sealed record TodayPlanRequest(
    IReadOnlyList<TodayPlanItemInput> Items,
    DateTimeOffset LocalNow,
    string TimeZoneDisplayName,
    int MaxOutputTokens = 2048,
    IReadOnlyList<TodayPlanOccupiedBlock>? OccupiedBlocks = null);

public sealed record TodayPlanBlock(
    Guid Id,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Reason);

public sealed record TodayPlanResult(
    TodayPlanStatus Status,
    IReadOnlyList<TodayPlanBlock> Blocks,
    AiErrorCategory ErrorCategory,
    string? ErrorMessage,
    string? Suggestion)
{
    public static TodayPlanResult DraftReady(IReadOnlyList<TodayPlanBlock> blocks) =>
        new(TodayPlanStatus.DraftReady, blocks, AiErrorCategory.None, null, null);

    public static TodayPlanResult Failed(AiErrorCategory category, string message, string suggestion) =>
        new(TodayPlanStatus.Failed, Array.Empty<TodayPlanBlock>(), category, message, suggestion);
}

public interface ITodayPlanAiClient
{
    Task<TodayPlanResult> PlanTodayAsync(
        string endpoint,
        string model,
        string apiKey,
        TodayPlanRequest request,
        CancellationToken cancellationToken);
}

public sealed partial class OpenAiCompatibleTodoClient : ITodayPlanAiClient
{
    public async Task<TodayPlanResult> PlanTodayAsync(
        string endpoint,
        string model,
        string apiKey,
        TodayPlanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestError = ValidatePlanRequest(request);
        if (requestError is not null) return requestError;
        if (!OpenAiCompatibleClient.IsValidEndpoint(endpoint))
            return TodayPlanResult.Failed(AiErrorCategory.BadEndpoint, "AI 服务地址无效。", "请先在设置中检查并测试服务地址。");
        if (string.IsNullOrWhiteSpace(model))
            return TodayPlanResult.Failed(AiErrorCategory.MissingField, "AI 模型未配置。", "请先在设置中填写模型并测试连接。");
        if (string.IsNullOrWhiteSpace(apiKey))
            return TodayPlanResult.Failed(AiErrorCategory.MissingField, "AI API Key 未配置。", "请先在设置中保存通过测试的 AI 配置。");

        var selectedItems = request.Items.Select(item => new
        {
            id = item.Id,
            title = item.Title,
            notes = item.Notes,
            dueAt = item.DueAt,
        }).ToArray();
        var occupiedBlocks = (request.OccupiedBlocks ?? Array.Empty<TodayPlanOccupiedBlock>())
            .Select(block => new { startAt = block.StartAt, endAt = block.EndAt })
            .ToArray();
        var payload = new
        {
            model,
            temperature = 0,
            max_tokens = Math.Clamp(request.MaxOutputTokens, 256, 2048),
            messages = new object[]
            {
                new { role = "system", content = BuildTodayPlanPrompt(request.LocalNow, request.TimeZoneDisplayName) },
                new { role = "user", content = JsonSerializer.Serialize(new { selectedItems, occupiedBlocks }) },
            },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsEndpoint(endpoint));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return TodayPlanFailureForStatus(response.StatusCode);
            var responseBody = await BoundedAiResponse.ReadAsync(response.Content, 256 * 1024, timeout.Token)
                .ConfigureAwait(false);
            return ParseTodayPlanResponse(responseBody, request);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return TodayPlanResult.Failed(AiErrorCategory.Timeout, "AI 安排超时。", "请稍后重试，或使用本地安排。");
        }
        catch (HttpRequestException)
        {
            return TodayPlanResult.Failed(AiErrorCategory.NetworkUnreachable, "无法连接 AI 服务。", "请检查网络，或使用本地安排。");
        }
        catch (JsonException)
        {
            return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "AI 返回的安排无法解析。", "请重试，或使用本地安排。");
        }
        catch
        {
            return TodayPlanResult.Failed(AiErrorCategory.Unknown, "AI 安排出现未分类错误。", "请稍后重试，或使用本地安排。");
        }
    }

    public static TodayPlanResult CreateLocalTodayPlan(TodayPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestError = ValidatePlanRequest(request);
        if (requestError is not null) return requestError;

        var localDate = request.LocalNow.Date;
        var cursor = RoundUpToQuarterHour(request.LocalNow, strictlyAfter: true);
        var occupied = (request.OccupiedBlocks ?? Array.Empty<TodayPlanOccupiedBlock>())
            .OrderBy(block => block.StartAt)
            .ToArray();
        var ordered = request.Items
            .OrderBy(item => item.DueAt?.ToOffset(request.LocalNow.Offset).Date == localDate ? 0 : item.DueAt is null ? 2 : 1)
            .ThenBy(item => item.DueAt ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var blocks = new List<TodayPlanBlock>(ordered.Length);
        foreach (var item in ordered)
        {
            DateTimeOffset end;
            while (true)
            {
                end = cursor.AddMinutes(30);
                var conflict = occupied.FirstOrDefault(block => BlocksOverlap(cursor, end, block.StartAt, block.EndAt));
                if (conflict is null) break;
                cursor = RoundUpToQuarterHour(conflict.EndAt, strictlyAfter: false);
            }
            if (end.Date != localDate)
                return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "今天剩余空闲时间不足以安排全部选中事项。", "请减少选中项、调整既有安排，或明天再安排剩余事项。");
            blocks.Add(new TodayPlanBlock(item.Id, cursor, end, "本地按截止时间排序，避让既有安排并预留 30 分钟专注时间。"));
            cursor = end;
        }
        return TodayPlanResult.DraftReady(blocks);
    }

    public static TodayPlanResult ParseTodayPlanResponse(string responseBody, TodayPlanRequest request)
    {
        using var root = JsonDocument.Parse(responseBody);
        if (root.RootElement.ValueKind != JsonValueKind.Object
            || !root.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || choices[0].ValueKind != JsonValueKind.Object
            || !choices[0].TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
            throw new JsonException("Missing today plan response.");
        return ParseTodayPlanJson(StripCodeFence(content.GetString() ?? string.Empty), request);
    }

    public static TodayPlanResult ParseTodayPlanJson(string json, TodayPlanRequest request)
    {
        var wire = JsonSerializer.Deserialize<TodayPlanWireResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        if (wire?.Blocks is null)
            return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "AI 没有返回完整安排。", "请重试，或使用本地安排。");

        var expected = request.Items.Select(item => item.Id).ToHashSet();
        if (wire.Blocks.Count != expected.Count || wire.Blocks.Select(block => block.Id).Distinct().Count() != wire.Blocks.Count
            || wire.Blocks.Any(block => block.Id is null || !expected.Contains(block.Id.Value)))
            return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "AI 返回了缺失、重复或未选中的待办。", "数据未改变，请重新生成安排。");

        var blocks = new List<TodayPlanBlock>(wire.Blocks.Count);
        foreach (var block in wire.Blocks)
        {
            if (!TryParseAbsolute(block.StartAt, out var start) || !TryParseAbsolute(block.EndAt, out var end))
                return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "AI 返回的时间不是完整绝对时间。", "数据未改变，请重新生成安排。");
            if (start.Offset != request.LocalNow.Offset || end.Offset != request.LocalNow.Offset
                || start.Date != request.LocalNow.Date || end.Date != request.LocalNow.Date
                || start <= request.LocalNow || end <= start
                || end - start < TimeSpan.FromMinutes(15) || end - start > TimeSpan.FromHours(4))
                return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "AI 返回的时间超出今天的安全范围。", "每项应为今天未来的 15 分钟至 4 小时时间块。");
            var reason = string.IsNullOrWhiteSpace(block.Reason) ? "按当前待办内容安排。" : block.Reason.Trim();
            if (reason.Length > 160) reason = reason[..160];
            blocks.Add(new TodayPlanBlock(block.Id!.Value, start, end, reason));
        }

        var ordered = blocks.OrderBy(block => block.StartAt).ToArray();
        for (var index = 1; index < ordered.Length; index++)
            if (ordered[index].StartAt < ordered[index - 1].EndAt)
                return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "AI 返回的时间块互相重叠。", "数据未改变，请重新生成安排。");
        var occupied = request.OccupiedBlocks ?? Array.Empty<TodayPlanOccupiedBlock>();
        if (ordered.Any(block => occupied.Any(existing =>
                BlocksOverlap(block.StartAt, block.EndAt, existing.StartAt, existing.EndAt))))
            return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "AI 返回的时间块与既有安排重叠。", "数据未改变，请重新生成安排。");
        return TodayPlanResult.DraftReady(ordered);
    }

    private static TodayPlanResult? ValidatePlanRequest(TodayPlanRequest request)
    {
        if (request.Items is null || request.Items.Count is < 1 or > 12)
            return TodayPlanResult.Failed(AiErrorCategory.MissingField, "请选择 1 至 12 条待处理事项。", "只会发送你明确勾选的事项。");
        if (request.Items.Any(item => item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Title))
            || request.Items.Select(item => item.Id).Distinct().Count() != request.Items.Count)
            return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "选中事项包含无效或重复标识。", "请刷新待办后重新选择。");
        if (request.OccupiedBlocks?.Any(block =>
                block.StartAt.Offset != request.LocalNow.Offset
                || block.EndAt.Offset != request.LocalNow.Offset
                || block.StartAt.Date != request.LocalNow.Date
                || block.EndAt.Date != request.LocalNow.Date
                || block.EndAt <= block.StartAt) == true)
            return TodayPlanResult.Failed(AiErrorCategory.InvalidResponse, "既有安排包含无效时间块。", "请刷新待办后重新生成安排。");
        return null;
    }

    private static bool BlocksOverlap(
        DateTimeOffset firstStart,
        DateTimeOffset firstEnd,
        DateTimeOffset secondStart,
        DateTimeOffset secondEnd) => firstStart < secondEnd && firstEnd > secondStart;

    private static DateTimeOffset RoundUpToQuarterHour(DateTimeOffset value, bool strictlyAfter)
    {
        var minute = new DateTimeOffset(
            value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);
        var isExactBoundary = value == minute && value.Minute % 15 == 0;
        if (isExactBoundary && !strictlyAfter) return minute;
        var minutes = value.Minute % 15 == 0 ? 15 : 15 - value.Minute % 15;
        return minute.AddMinutes(minutes);
    }

    private static bool TryParseAbsolute(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(value.Trim(), @"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result);
    }

    private static string BuildTodayPlanPrompt(DateTimeOffset localNow, string timeZoneDisplayName) =>
        $$"""
        你是本地待办的今日安排器。当前本地绝对时间：{{localNow:yyyy-MM-dd'T'HH:mm:sszzz}}；时区：{{timeZoneDisplayName}}。
        用户消息包含他明确勾选的待办 selectedItems，以及不含待办正文或标识的匿名 occupiedBlocks。
        必须为每个输入 id 返回且只返回一个时间块，不得添加、删除、修改或猜测 id。
        所有时间块必须位于今天且晚于当前时间，使用当前 UTC 偏移，不得互相重叠，也不得与 occupiedBlocks 重叠；单项 15 分钟至 4 小时。
        只返回 JSON，不要 Markdown：
        { "blocks": [{ "id": "原 id", "startAt": "ISO 8601 绝对时间", "endAt": "ISO 8601 绝对时间", "reason": "不超过 160 字的简短安排理由" }] }
        不要声称已经写入待办。最终写入仍由用户逐项勾选并确认。
        """;

    private static TodayPlanResult TodayPlanFailureForStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => TodayPlanResult.Failed(AiErrorCategory.AuthFailed, "AI 鉴权失败。", "请在设置中检查 API Key，或使用本地安排。"),
        HttpStatusCode.Forbidden => TodayPlanResult.Failed(AiErrorCategory.Forbidden, "当前账户无权使用该 AI 模型。", "请检查模型权限，或使用本地安排。"),
        HttpStatusCode.TooManyRequests => TodayPlanResult.Failed(AiErrorCategory.RateLimited, "AI 服务请求过于频繁。", "请稍后重试，或使用本地安排。"),
        HttpStatusCode.BadRequest or HttpStatusCode.NotFound => TodayPlanResult.Failed(AiErrorCategory.BadEndpoint, "AI 服务地址、模型或请求格式不兼容。", "请检查设置，或使用本地安排。"),
        _ => TodayPlanResult.Failed(AiErrorCategory.Unknown, $"AI 服务暂时不可用（HTTP {(int)statusCode}）。", "请稍后重试，或使用本地安排。"),
    };

    private sealed class TodayPlanWireResult
    {
        [JsonPropertyName("blocks")]
        public List<TodayPlanWireBlock>? Blocks { get; init; }
    }

    private sealed class TodayPlanWireBlock
    {
        [JsonPropertyName("id")]
        public Guid? Id { get; init; }
        [JsonPropertyName("startAt")]
        public string? StartAt { get; init; }
        [JsonPropertyName("endAt")]
        public string? EndAt { get; init; }
        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }
}
