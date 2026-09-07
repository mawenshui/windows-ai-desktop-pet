using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiPet.Todos;

namespace AiPet.AI;

public enum AiTodoOperation
{
    Create = 0,
    Update = 1,
    Complete = 2,
    Delete = 3,
    Snooze = 4,
}

public enum AiTodoParseStatus
{
    DraftReady = 0,
    NeedsClarification = 1,
    Failed = 2,
}

public sealed record AiTodoParseRequest(
    string UserText,
    DateTimeOffset LocalNow,
    string TimeZoneDisplayName,
    int MaxOutputTokens = 1024);

public sealed record AiTodoDraft(
    AiTodoOperation Operation,
    string? Title,
    string? TargetTitle,
    string? Notes,
    DateTimeOffset? DueAt,
    DateTimeOffset? ReminderAt,
    bool ClearDue,
    bool ClearReminder,
    int? SnoozeMinutes,
    RecurrenceRule? Recurrence = null,
    IReadOnlyList<DateTimeOffset>? AdditionalReminderTimes = null);

public sealed record AiTodoParseResult(
    AiTodoParseStatus Status,
    AiTodoDraft? Draft,
    string? Clarification,
    AiErrorCategory ErrorCategory,
    string? ErrorMessage,
    string? Suggestion)
{
    public static AiTodoParseResult DraftReady(AiTodoDraft draft) =>
        new(AiTodoParseStatus.DraftReady, draft, null, AiErrorCategory.None, null, null);

    public static AiTodoParseResult NeedsClarification(string message) =>
        new(AiTodoParseStatus.NeedsClarification, null, message, AiErrorCategory.None, null, null);

    public static AiTodoParseResult Failed(
        AiErrorCategory category,
        string message,
        string suggestion) =>
        new(AiTodoParseStatus.Failed, null, null, category, message, suggestion);
}

public interface ITodoAiClient
{
    Task<AiTodoParseResult> ParseAsync(
        string endpoint,
        string model,
        string apiKey,
        AiTodoParseRequest request,
        CancellationToken cancellationToken);
}

public sealed class OpenAiCompatibleTodoClient : ITodoAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _httpClient;

    public OpenAiCompatibleTodoClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    }

    public async Task<AiTodoParseResult> ParseAsync(
        string endpoint,
        string model,
        string apiKey,
        AiTodoParseRequest request,
        CancellationToken cancellationToken)
    {
        if (!OpenAiCompatibleClient.IsValidEndpoint(endpoint))
            return AiTodoParseResult.Failed(
                AiErrorCategory.BadEndpoint,
                "AI 服务地址无效。",
                "请先在设置中检查并测试服务地址。");
        if (string.IsNullOrWhiteSpace(model))
            return AiTodoParseResult.Failed(
                AiErrorCategory.MissingField,
                "AI 模型未配置。",
                "请先在设置中填写模型并测试连接。");
        if (string.IsNullOrWhiteSpace(apiKey))
            return AiTodoParseResult.Failed(
                AiErrorCategory.MissingField,
                "AI API Key 未配置。",
                "请先在设置中保存通过测试的 AI 配置。");
        if (string.IsNullOrWhiteSpace(request.UserText))
            return AiTodoParseResult.Failed(
                AiErrorCategory.MissingField,
                "请输入一句待办或提醒。",
                "例如：明天下午 3 点提醒我提交周报。");

        var payload = new
        {
            model,
            temperature = 0,
            max_tokens = Math.Clamp(request.MaxOutputTokens, 128, 2048),
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt(request.LocalNow, request.TimeZoneDisplayName),
                },
                new { role = "user", content = request.UserText.Trim() },
            },
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildChatCompletionsEndpoint(endpoint));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return FailureForStatus(response.StatusCode);

            var responseBody = await BoundedAiResponse.ReadAsync(response.Content,256*1024,timeout.Token)
                .ConfigureAwait(false);
            return ParseResponse(responseBody, request.LocalNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiTodoParseResult.Failed(
                AiErrorCategory.Timeout,
                "AI 解析超时。",
                "请稍后重试，或转为手动填写。");
        }
        catch (HttpRequestException)
        {
            return AiTodoParseResult.Failed(
                AiErrorCategory.NetworkUnreachable,
                "无法连接 AI 服务。",
                "请检查网络和服务地址后重试，或转为手动填写。");
        }
        catch (JsonException)
        {
            return AiTodoParseResult.Failed(
                AiErrorCategory.InvalidResponse,
                "AI 返回的内容无法解析。",
                "请重试并换一种说法，或转为手动填写。");
        }
        catch (Exception)
        {
            return AiTodoParseResult.Failed(
                AiErrorCategory.Unknown,
                "AI 解析出现未分类错误。",
                "请稍后重试，或转为手动填写。");
        }
    }

    public static string BuildChatCompletionsEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim().TrimEnd('/');
        if (Regex.IsMatch(normalized, @"/v\d+$", RegexOptions.IgnoreCase))
            return normalized + "/chat/completions";
        return normalized + "/v1/chat/completions";
    }

    public static AiTodoParseResult ParseResponse(string responseBody, DateTimeOffset localNow)
    {
        using var root = JsonDocument.Parse(responseBody);
        if(root.RootElement.ValueKind!=JsonValueKind.Object || !root.RootElement.TryGetProperty("choices",out var choices) || choices.ValueKind!=JsonValueKind.Array || choices.GetArrayLength()==0 ||
            choices[0].ValueKind!=JsonValueKind.Object || !choices[0].TryGetProperty("message",out var message) || message.ValueKind!=JsonValueKind.Object ||
            !message.TryGetProperty("content",out var body) || body.ValueKind!=JsonValueKind.String)
            throw new JsonException("Missing structured response.");
        var content = body.GetString();
        if (string.IsNullOrWhiteSpace(content))
            return AiTodoParseResult.Failed(
                AiErrorCategory.Unknown,
                "AI 没有返回可用内容。",
                "请重试，或转为手动填写。");
        return ParseDraftJson(StripCodeFence(content), localNow);
    }

    public static AiTodoParseResult ParseDraftJson(string json, DateTimeOffset localNow)
    {
        var wire = JsonSerializer.Deserialize<AiTodoWireResult>(json, JsonOptions);
        if (wire is null)
            return AiTodoParseResult.Failed(
                AiErrorCategory.Unknown,
                "AI 草稿为空。",
                "请重试，或转为手动填写。");

        if (string.Equals(wire.Status, "clarification", StringComparison.OrdinalIgnoreCase))
        {
            var clarification = string.IsNullOrWhiteSpace(wire.Clarification)
                ? "请补充会影响创建结果的日期或时间。"
                : wire.Clarification.Trim();
            return AiTodoParseResult.NeedsClarification(clarification);
        }

        if (!TryParseOperation(wire.Operation, out var operation))
            return AiTodoParseResult.NeedsClarification("我还不能确定要创建还是修改待办，请换一种说法。");

        if (operation == AiTodoOperation.Create && string.IsNullOrWhiteSpace(wire.Title))
            return AiTodoParseResult.NeedsClarification("这条待办的标题是什么？");
        if (operation != AiTodoOperation.Create && string.IsNullOrWhiteSpace(wire.TargetTitle))
            return AiTodoParseResult.NeedsClarification("要操作哪一条待办？请提供待办标题。");

        var dueAt = ParseDateTimeOffset(wire.DueAt);
        var reminderAt = ParseDateTimeOffset(wire.ReminderAt);
        if (!string.IsNullOrWhiteSpace(wire.DueAt) && dueAt is null)
            return AiTodoParseResult.NeedsClarification("截止时间无法确定，请提供完整日期和时间。");
        if (!string.IsNullOrWhiteSpace(wire.ReminderAt) && reminderAt is null)
            return AiTodoParseResult.NeedsClarification("提醒时间无法确定，请提供完整日期和时间。");
        if (dueAt is { } due && due <= localNow)
            return AiTodoParseResult.NeedsClarification("截止时间已经过去，请提供一个未来时间。");
        if (reminderAt is { } reminder && reminder <= localNow)
            return AiTodoParseResult.NeedsClarification("提醒时间已经过去，请提供一个未来时间。");
        if (operation == AiTodoOperation.Snooze && wire.SnoozeMinutes is null && reminderAt is null)
            return AiTodoParseResult.NeedsClarification("要稍后多久再次提醒？");

        try
        {
            if (wire.Recurrence is { } rule)
            {
                RecurrenceCalculator.Validate(rule);
                if (rule.Kind != RecurrenceKind.None && (reminderAt is null || string.IsNullOrEmpty(rule.TimeZoneId)))
                    return AiTodoParseResult.NeedsClarification("重复提醒需要首次时间和明确时区。");
                if (rule.EndsAt is { } end && end < reminderAt)
                    return AiTodoParseResult.NeedsClarification("规则结束时间不能早于首次提醒。");
            }
            if (wire.AdditionalReminderTimes is { } times && (times.Count > 32 || times.Any(time => time <= localNow)))
                return AiTodoParseResult.NeedsClarification("额外提醒最多 32 次且必须是未来时间。");
        }
        catch (TodoValidationException ex) { return AiTodoParseResult.NeedsClarification(ex.Message); }
        return AiTodoParseResult.DraftReady(new AiTodoDraft(
            operation,
            wire.Title?.Trim(),
            wire.TargetTitle?.Trim(),
            wire.Notes?.Trim(),
            dueAt,
            reminderAt,
            wire.ClearDue,
            wire.ClearReminder,
            wire.SnoozeMinutes, wire.Recurrence, wire.AdditionalReminderTimes));
    }

    private static string BuildSystemPrompt(DateTimeOffset localNow, string timeZoneDisplayName) =>
        $$"""
        你是 Windows 桌面待办解析器，只处理创建、修改、完成、删除和稍后提醒意图。
        当前本地绝对时间：{{localNow:yyyy-MM-dd'T'HH:mm:sszzz}}
        当前时区：{{timeZoneDisplayName}}
        只返回一个 JSON 对象，不要 Markdown，不要解释。结构：
        {
          "status": "draft" 或 "clarification",
          "operation": "create" | "update" | "complete" | "delete" | "snooze",
          "title": 创建后的标题或 null,
          "targetTitle": 修改类操作的原待办标题或 null,
          "notes": 备注或 null,
          "dueAt": ISO 8601 绝对时间或 null,
          "reminderAt": ISO 8601 绝对时间或 null,
          "clearDue": false,
          "clearReminder": false,
          "snoozeMinutes": 整数或 null,
          "recurrence": null 或 { "kind": "Daily" | "Weekly" | "Weekdays" | "CustomDays" | "None", "interval": 1, "timeZoneId": "有效系统时区ID", "daysOfWeek": ["Monday"], "endsAt": null },
          "additionalReminderTimes": null 或 ISO 8601 绝对时间数组,
          "clarification": 只在 status=clarification 时填写
        }
        相对日期必须基于当前本地时间换算并保留当前 UTC 偏移。像“下午”但没有具体钟点、互相冲突、存在多种合理解释或时间已经过去时，status 必须为 clarification，且只追问最少必要字段，绝不猜测。不要执行操作，不要声称已经创建或修改。
        """;

    private static AiTodoParseResult FailureForStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => AiTodoParseResult.Failed(
            AiErrorCategory.AuthFailed,
            "AI 鉴权失败。",
            "请在设置中检查 API Key 并重新测试连接。"),
        HttpStatusCode.Forbidden => AiTodoParseResult.Failed(
            AiErrorCategory.Forbidden,
            "当前账户无权使用该 AI 模型。",
            "请检查服务账户权限或更换模型。"),
        HttpStatusCode.TooManyRequests => AiTodoParseResult.Failed(
            AiErrorCategory.RateLimited,
            "AI 服务请求过于频繁。",
            "请稍后重试，或转为手动填写。"),
        HttpStatusCode.BadRequest or HttpStatusCode.NotFound => AiTodoParseResult.Failed(
            AiErrorCategory.BadEndpoint,
            "AI 服务地址、模型或请求格式不兼容。",
            "请在设置中检查地址和模型，或转为手动填写。"),
        _ => AiTodoParseResult.Failed(
            AiErrorCategory.Unknown,
            $"AI 服务暂时不可用（HTTP {(int)statusCode}）。",
            "请稍后重试，或转为手动填写。"),
    };

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var result)
            ? result
            : null;
    }

    private static bool TryParseOperation(string? value, out AiTodoOperation operation) =>
        Enum.TryParse(value, ignoreCase: true, out operation)
        && Enum.IsDefined(operation);

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine) return trimmed;
        return trimmed[(firstNewLine + 1)..lastFence].Trim();
    }

    private sealed class AiTodoWireResult
    {
        [JsonPropertyName("recurrence")]
        public RecurrenceRule? Recurrence { get; init; }
        [JsonPropertyName("additionalReminderTimes")]
        public IReadOnlyList<DateTimeOffset>? AdditionalReminderTimes { get; init; }
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("operation")]
        public string? Operation { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("targetTitle")]
        public string? TargetTitle { get; init; }

        [JsonPropertyName("notes")]
        public string? Notes { get; init; }

        [JsonPropertyName("dueAt")]
        public string? DueAt { get; init; }

        [JsonPropertyName("reminderAt")]
        public string? ReminderAt { get; init; }

        [JsonPropertyName("clearDue")]
        public bool ClearDue { get; init; }

        [JsonPropertyName("clearReminder")]
        public bool ClearReminder { get; init; }

        [JsonPropertyName("snoozeMinutes")]
        public int? SnoozeMinutes { get; init; }

        [JsonPropertyName("clarification")]
        public string? Clarification { get; init; }
    }
}
