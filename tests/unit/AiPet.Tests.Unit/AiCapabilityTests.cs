using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AiPet.AI;
using AiPet.Storage;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class AiCapabilityTests
{
    [Theory]
    [InlineData("{\"data\":[{\"id\":\"model\"}]}", AiErrorCategory.None)]
    [InlineData("{\"data\":[{\"id\":\"another\"}]}", AiErrorCategory.ModelUnavailable)]
    [InlineData("<html>ok</html>", AiErrorCategory.InvalidResponse)]
    [InlineData("{}", AiErrorCategory.InvalidResponse)]
    [InlineData("[]", AiErrorCategory.InvalidResponse)]
    public async Task Http_200_is_not_enough_to_verify_model(string body,AiErrorCategory category)
    {
        using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)})));
        var result=await new OpenAiCompatibleClient(http).TestConnectionAsync("https://example.test","model","test-only-key",CancellationToken.None);
        Assert.Equal(category,result.ErrorCategory);
        Assert.Equal("未运行",result.GenerationCheck);
    }
    [Fact]
    public async Task Model_discovery_success_does_not_mask_generation_rejection()
    {
        using var http=new HttpClient(new Handler((request,_)=>Task.FromResult(request.Method==HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"model\"}]}")}
            : new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("private-body")})));
        var client=new OpenAiCompatibleClient(http);
        Assert.Equal(AiConnectionStatus.Connected,(await client.TestConnectionAsync("https://example.test","model","test-only-key",default)).Status);
        var generated=await client.VerifyGenerationAsync("https://example.test","model","test-only-key",default);
        Assert.Equal(AiErrorCategory.Forbidden,generated.ErrorCategory);
        Assert.DoesNotContain("private-body",generated.CapabilitySummary + generated.LocalizedMessage);
    }
    [Fact]
    public async Task Oversized_response_is_rejected_for_discovery_and_generation()
    {
        using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',2*1024*1024+1))})));
        var client=new OpenAiCompatibleClient(http);
        Assert.Equal(AiErrorCategory.InvalidResponse,(await client.TestConnectionAsync("https://example.test","model","fixture-key",default)).ErrorCategory);
        Assert.Equal(AiErrorCategory.InvalidResponse,(await client.VerifyGenerationAsync("https://example.test","model","fixture-key",default)).ErrorCategory);
    }
    [Fact]
    public async Task Generation_validation_uses_fixed_text_bounded_output_and_no_local_items()
    {
        using var http=new HttpClient(new Handler(async (request,ct)=>
        {
            using var payload=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(256,payload.RootElement.GetProperty("max_tokens").GetInt32());
            Assert.Contains("连通性测试",payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            var draft="{\"status\":\"draft\",\"operation\":\"create\",\"title\":\"连通性测试\"}";
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new {choices=new[]{new {message=new {content=draft}}}}))};
        }));
        var result=await new OpenAiCompatibleClient(http).VerifyGenerationAsync("https://example.test","model","test-only-key",default);
        Assert.Equal(AiConnectionStatus.Connected,result.Status);
        Assert.Contains("未写入",result.GenerationCheck);
    }
    [Fact]
    public async Task User_cancellation_propagates_in_both_checks()
    {
        using var cts=new CancellationTokenSource(); cts.Cancel();
        using var http=new HttpClient(new Handler((_,ct)=>Task.FromCanceled<HttpResponseMessage>(ct)));
        var client=new OpenAiCompatibleClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.TestConnectionAsync("https://example.test","model","test-only-key",cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.VerifyGenerationAsync("https://example.test","model","test-only-key",cts.Token));
    }
    [Fact]
    public void Export_has_no_secret_references_and_import_preserves_active_profile()
    {
        var root=Path.Combine(Path.GetTempPath(),"aipet-ai-exchange-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var store=new SettingsStore(root); var settings=store.Load();
            settings.Ai.Profiles.Add(new() {Id="old",DisplayName="daily",Endpoint="https://example.test",Model="model",SecretTargetName="private-reference"});
            settings.Ai.ActiveProfileId="old"; store.Save(settings);
            var file=Path.Combine(root,"export.json"); AiConfigurationExchange.Export(file,settings.Ai.Profiles);
            Assert.DoesNotContain("private-reference",File.ReadAllText(file)); Assert.DoesNotContain("Key",File.ReadAllText(file));
            var preview=AiConfigurationExchange.Preview(file);
            Assert.Equal(0,AiConfigurationExchange.Import(store,preview,AiImportConflict.Skip));
            Assert.Equal(1,AiConfigurationExchange.Import(store,preview,AiImportConflict.Rename));
            Assert.Equal("old",store.Load().Ai.ActiveProfileId);
            Assert.Equal("Untested",store.Load().Ai.Profiles[1].LastStatus);
            var emptyStore=new SettingsStore(Path.Combine(root,"empty"));
            AiConfigurationExchange.Import(emptyStore,preview,AiImportConflict.Rename);
            Assert.Null(emptyStore.Load().Ai.ActiveProfileId);
        }
        finally { Directory.Delete(root,true); }
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>response(request,cancellationToken); }
}
