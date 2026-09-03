using System.Net;
using System.Net.Http;
using System.Text;
using AiPet.AI;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class AiTodoClientTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 9, 0, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("https://example.test/v1/", "https://example.test/v1/chat/completions")]
    [InlineData("https://example.test/api/v4", "https://example.test/api/v4/chat/completions")]
    public void Chat_endpoint_is_built_for_base_and_versioned_urls(string endpoint, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleTodoClient.BuildChatCompletionsEndpoint(endpoint));
    }

    [Fact]
    public void Draft_json_parses_absolute_title_due_and_reminder()
    {
        var result = OpenAiCompatibleTodoClient.ParseDraftJson(
            """
            {"status":"draft","operation":"create","title":"提交周报","targetTitle":null,"notes":"附数据","dueAt":"2026-08-29T17:00:00+08:00","reminderAt":"2026-08-29T15:00:00+08:00","clearDue":false,"clearReminder":false,"snoozeMinutes":null,"clarification":null}
            """,
            Now);

        Assert.Equal(AiTodoParseStatus.DraftReady, result.Status);
        Assert.Equal("提交周报", result.Draft?.Title);
        Assert.Equal(15, result.Draft?.ReminderAt?.Hour);
        Assert.Equal(TimeSpan.FromHours(8), result.Draft?.ReminderAt?.Offset);
    }

    [Fact]
    public void Past_time_becomes_clarification_instead_of_a_draft()
    {
        var result = OpenAiCompatibleTodoClient.ParseDraftJson(
            """
            {"status":"draft","operation":"create","title":"开会","reminderAt":"2026-08-28T08:30:00+08:00"}
            """,
            Now);

        Assert.Equal(AiTodoParseStatus.NeedsClarification, result.Status);
        Assert.Contains("已经过去", result.Clarification, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_is_classified_without_exposing_response_body()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("secret upstream response", Encoding.UTF8),
        });
        var client = new OpenAiCompatibleTodoClient(new HttpClient(handler));

        var result = await client.ParseAsync(
            "https://example.test",
            "model",
            "test-api-key",
            new AiTodoParseRequest("明天提醒我提交周报", Now, "China Standard Time"),
            CancellationToken.None);

        Assert.Equal(AiTodoParseStatus.Failed, result.Status);
        Assert.Equal(AiErrorCategory.RateLimited, result.ErrorCategory);
        Assert.DoesNotContain("secret upstream", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_fake_server_json_is_reported_without_overwriting_user_data()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json"),
        });
        var client = new OpenAiCompatibleTodoClient(new HttpClient(handler));

        var result = await client.ParseAsync(
            "https://example.test",
            "model",
            "test-api-key",
            new AiTodoParseRequest("明天提醒我提交周报", Now, "China Standard Time"),
            CancellationToken.None);

        Assert.Equal(AiTodoParseStatus.Failed, result.Status);
        Assert.Equal(AiErrorCategory.Unknown, result.ErrorCategory);
        Assert.Null(result.Draft);
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
