using System.Collections.Generic;

namespace AiPet.AI;

/// <summary>
/// Catalog of built-in AI providers. Selecting a preset pre-fills the
/// endpoint / model fields in the settings UI; the user can override any
/// of them before saving (per the 2026-08-26 product decision).
/// </summary>
public sealed record AiProviderDescriptor(
    string Id,
    string DisplayName,
    string InfoUrl,
    string DefaultEndpoint,
    string DefaultModel,
    string Notes,
    IReadOnlyList<string>? SuggestedModels = null,
    bool SupportsStructuredJson = true,
    bool SupportsModelDiscovery = false,
    string ValidationScope = "鉴权、端点可达性与最小结构化请求");

public static class AiProviders
{
    public static readonly IReadOnlyList<AiProviderDescriptor> Builtin = new[]
    {
        new AiProviderDescriptor(
            "deepseek", "DeepSeek (默认)",
            "https://platform.deepseek.com/",
            "https://api.deepseek.com", "deepseek-chat",
            "OpenAI 兼容;测试用 POST {endpoint}/v1/models"),
        new AiProviderDescriptor(
            "zhipu", "智谱 BigModel",
            "https://open.bigmodel.cn/",
            "https://open.bigmodel.cn/api/paas/v4", "glm-4.5",
            "OpenAI 兼容;字段可能随厂商更新,选完后所有字段仍可改"),
        new AiProviderDescriptor(
            "qwen", "通义千问 Qwen",
            "https://help.aliyun.com/zh/model-studio/developer-reference/compatibility-of-openai-with-dashscope",
            "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus",
            "DashScope 兼容模式"),
        new AiProviderDescriptor(
            "moonshot", "月之暗面 Moonshot",
            "https://platform.moonshot.cn/",
            "https://api.moonshot.cn/v1", "moonshot-v1-8k",
            "OpenAI 兼容"),
        new AiProviderDescriptor(
            "qianfan", "百度千帆",
            "https://cloud.baidu.com/doc/qianfan/s/hlrk4akp7",
            "https://qianfan.baidubce.com/v2", "ernie-4.5-8k",
            "v2 端点;OpenAI 兼容"),
        new AiProviderDescriptor(
            "hunyuan", "腾讯混元",
            "https://cloud.tencent.com/document/product/1729",
            "https://api.hunyuan.tencent.com/v3", "hunyuan-turbos",
            "v3 端点;OpenAI 兼容"),
        new AiProviderDescriptor(
            "yi", "零一万物 Yi",
            "https://platform.lingyiwanwu.com/",
            "https://api.lingyiwanwu.com/v1", "yi-large",
            "OpenAI 兼容"),
        new AiProviderDescriptor(
            "siliconflow", "硅基流动 SiliconFlow",
            "https://siliconflow.cn/",
            "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-72B-Instruct",
            "聚合多种开源模型"),
        new AiProviderDescriptor(
            "custom", "自定义 (OpenAI 兼容)",
            "",
            "", "",
            "端点、模型、协议版本全部由你填写;不绑定任何厂商"),
    };

    public static AiProviderDescriptor? FindById(string id)
    {
        foreach (var p in Builtin) if (p.Id == id) return p;
        return null;
    }

    public const string CustomId = "custom";
}
