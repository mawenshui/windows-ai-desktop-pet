using System.IO;
using System.Text.Json.Nodes;
using AiPet.Prototypes;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PrototypeTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"aipet-prototypes-"+Guid.NewGuid().ToString("N"));
    public PrototypeTests()=>Directory.CreateDirectory(_root);
    private string Pack()
    {
        var repo=new DirectoryInfo(AppContext.BaseDirectory);
        while(repo is not null && !File.Exists(Path.Combine(repo.FullName,"VERSION"))) repo=repo.Parent;
        var source=Path.Combine(repo!.FullName,"assets","pets","RGS_8Directional");
        var destination=Path.Combine(_root,"source"); Directory.CreateDirectory(destination);
        foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
        { var target=Path.Combine(destination,Path.GetRelativePath(source,file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file,target); }
        return destination;
    }
    [Fact]
    public async Task Content_requires_separate_consent_bounds_encoding_and_purges_on_revoke()
    {
        var fixtures=Path.Combine(_root,"text"); Directory.CreateDirectory(fixtures);
        File.WriteAllText(Path.Combine(fixtures,"allowed.txt"),"anonymous needle");
        File.WriteAllText(Path.Combine(fixtures,"ignored.pdf"),"needle");
        File.WriteAllBytes(Path.Combine(fixtures,"invalid.txt"),new byte[] {0xff,0xff});
        File.WriteAllText(Path.Combine(fixtures,"large.txt"),new string('x',ControlledTextIndex.MaximumFileBytes+1));
        var data=Path.Combine(_root,"index"); using var index=new ControlledTextIndex(data);
        Assert.Throws<InvalidOperationException>(()=>index.Authorize(fixtures,false));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>index.BuildAsync());
        index.Authorize(fixtures,true);
        var result=await index.BuildAsync(); Assert.Equal(1,result.Indexed); Assert.Equal(3,result.Skipped);
        Assert.Equal("allowed.txt",Assert.Single(index.Search("needle")));
        using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>index.BuildAsync(cancelled.Token));
        Assert.Single(index.Search("needle")); index.Revoke(); Assert.Empty(index.Search("needle")); Assert.Empty(Directory.GetFiles(data));
    }
    [Fact]
    public async Task Revocation_cancels_rebuild_without_recreating_derived_data()
    {
        var fixtures=Path.Combine(_root,"text"); Directory.CreateDirectory(fixtures);
        for(var i=0;i<300;i++) File.WriteAllText(Path.Combine(fixtures,$"{i}.txt"),new string('x',20000));
        var data=Path.Combine(_root,"index"); using var index=new ControlledTextIndex(data); index.Authorize(fixtures,true);
        var work=index.BuildAsync(); index.Revoke();
        try { await work; } catch(OperationCanceledException) { }
        Assert.Empty(index.Search("x")); Assert.Empty(Directory.GetFiles(data));
    }
    [Fact]
    public void Pack_preview_import_duplicate_and_corruption_fallback_are_isolated()
    {
        var source=Pack(); var preview=StrictPetPack.Preview(source);
        Assert.Equal(247,preview.Frames); Assert.InRange(preview.DecodedBytes,1,StrictPetPack.MaximumDecodedBytes);
        var builtin=Path.Combine(_root,"builtin"); var library=new ExperimentalPetLibrary(Path.Combine(_root,"library"),builtin);
        library.Import(source,preview); Assert.Throws<InvalidOperationException>(()=>library.Import(source,preview));
        library.Enable(preview.Id); var active=library.ResolveActive(); Assert.NotEqual(builtin,active);
        File.WriteAllText(Path.Combine(active,"pet.json"),"broken"); Assert.Equal(builtin,library.ResolveActive());
        library.Remove(preview.Id); Assert.False(Directory.Exists(active)); Assert.True(File.Exists(Path.Combine(source,"pet.json")));
    }
    [Theory]
    [InlineData("path")]
    [InlineData("license")]
    [InlineData("missing")]
    [InlineData("oversized")]
    [InlineData("unknown")]
    public void Invalid_pack_never_enters_library(string variant)
    {
        var source=Pack(); var preview=StrictPetPack.Preview(source); var file=Path.Combine(source,"pet.json"); var json=JsonNode.Parse(File.ReadAllText(file))!;
        if(variant=="path") json["frameInventory"]!["filePathPattern"]="../<character>.png";
        if(variant=="license") json["source"]!["license"]="unknown";
        if(variant=="unknown") json["runScript"]="untrusted";
        File.WriteAllText(file,json.ToJsonString());
        var frame=Path.Combine(source,"frames","hero","idle_down_01.png");
        if(variant=="missing") File.Delete(frame);
        if(variant=="oversized") { var bytes=File.ReadAllBytes(frame); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16,4),100000); File.WriteAllBytes(frame,bytes); }
        var libraryRoot=Path.Combine(_root,"library"); var library=new ExperimentalPetLibrary(libraryRoot,source);
        Assert.ThrowsAny<Exception>(()=>library.Import(source,preview)); Assert.Empty(Directory.GetDirectories(libraryRoot));
    }
    public void Dispose()=>Directory.Delete(_root,true);
}
