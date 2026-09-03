using System.Net;
using System.Net.Http;
using AiPet.AI;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class AiEndpointTests
{
    [Theory]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/v1/models")]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", "https://dashscope.aliyuncs.com/compatible-mode/v1/models")]
    [InlineData("https://open.bigmodel.cn/api/paas/v4/", "https://open.bigmodel.cn/api/paas/v4/models")]
    public void Builds_models_endpoint_without_duplicate_version_segment(string endpoint, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleClient.BuildModelsEndpoint(endpoint));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiErrorCategory.AuthFailed)]
    [InlineData(HttpStatusCode.Forbidden, AiErrorCategory.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, AiErrorCategory.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, AiErrorCategory.Unknown)]
    public async Task Fake_server_statuses_are_classified_without_response_body_leaks(
        HttpStatusCode statusCode,
        AiErrorCategory expected)
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("upstream-secret-response"),
        }));
        var client = new OpenAiCompatibleClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(
            "https://example.test", "model", "test-api-key", CancellationToken.None);

        Assert.Equal(expected, result.ErrorCategory);
        Assert.DoesNotContain("upstream-secret-response", result.LocalizedMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, AiErrorCategory.Timeout)]
    [InlineData(false, AiErrorCategory.NetworkUnreachable)]
    public async Task Fake_server_transport_failures_are_classified(
        bool timeout,
        AiErrorCategory expected)
    {
        var handler = new StubHandler(_ => timeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("simulated timeout"))
            : Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated DNS failure")));
        var client = new OpenAiCompatibleClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(
            "https://example.test", "model", "test-api-key", CancellationToken.None);

        Assert.Equal(expected, result.ErrorCategory);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
