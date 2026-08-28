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
}
