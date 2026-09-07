using System.IO;
using AiPet.Search;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class SearchLifecycleTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"aipet-search-life-"+Guid.NewGuid().ToString("N"));
    public SearchLifecycleTests()=>Directory.CreateDirectory(_root);
    [Fact]
    public async Task Revoking_during_scan_cannot_resurrect_authorization_or_rows()
    {
        using var reached=new ManualResetEventSlim(); using var resume=new ManualResetEventSlim();
        IEnumerable<SearchItemRow> Scan(SearchRange range) { reached.Set(); resume.Wait(TimeSpan.FromSeconds(5)); yield return new(range.Id,"example",Path.Combine(range.Path,"example"),"example","",SearchItemKind.Other,0,DateTimeOffset.UtcNow); }
        using var search=new SearchService(Path.Combine(_root,"state","index.db"),Scan);
        search.AddRange(_root); var id=Assert.Single(search.ListRanges()).Id;
        var task=search.IndexRangeAsync(id);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(5))); search.RemoveRange(id); resume.Set();
        try { await task; } catch (OperationCanceledException) { }
        Assert.Null(search.GetRange(id)); Assert.Empty(search.Search("example"));
    }
    [Fact]
    public async Task Restart_restores_watchers_and_directory_move_updates_descendants()
    {
        var range=Path.Combine(_root,"files"); Directory.CreateDirectory(Path.Combine(range,"before"));
        File.WriteAllText(Path.Combine(range,"before","example.txt"),"fixture");
        var db=Path.Combine(_root,"state","index.db");
        using (var first=new SearchService(db)) { first.AddRange(range); await first.IndexRangeAsync(Assert.Single(first.ListRanges()).Id); }
        using var second=new SearchService(db);
        Directory.Move(Path.Combine(range,"before"),Path.Combine(range,"after"));
        File.WriteAllText(Path.Combine(range,"new.txt"),"fixture");
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow<deadline)
        {
            var found=second.Search("example");
            if (found.Count==1 && found[0].RelativePath.StartsWith("after") && second.Search("new.txt").Count==1) return;
            await Task.Delay(40);
        }
        Assert.Fail("Restarted watcher did not reconcile new files and moved subtrees.");
    }
    [Fact]
    public void Relative_path_mode_is_explicit_and_stable_across_pages()
    {
        using var index=new SearchIndex(Path.Combine(_root,"index.db"));
        var id=Guid.NewGuid(); var time=DateTimeOffset.UtcNow;
        index.InsertItems(Enumerable.Range(0,120).Select(number=>new SearchItemRow(id,"report.txt",$"C:\\fixture\\group\\{number:D3}\\report.txt",$"group/{number:D3}/report.txt",".txt",SearchItemKind.Document,0,time)).ToArray());
        Assert.Empty(index.Search("group",null));
        var first=index.Search("group",null,new SearchQueryOptions(Limit:50,Field:SearchField.RelativePath));
        var second=index.Search("group",null,new SearchQueryOptions(Limit:50,Offset:50,Field:SearchField.RelativePath));
        Assert.Equal(100,first.Concat(second).Select(item=>item.FullPath).Distinct().Count());
        Assert.All(first,item=>Assert.Contains("相对路径",item.MatchReason));
    }
    [Fact]
    public void Pins_persist_and_recent_history_is_optional_clearable_and_revoked_with_range()
    {
        var db=Path.Combine(_root,"index.db"); var range=SearchRange.For(_root);
        using (var index=new SearchIndex(db))
        {
            index.UpsertRange(range);
            index.InsertItems(new[] { new SearchItemRow(range.Id,"a",Path.Combine(_root,"a"),"a","",SearchItemKind.Other,0,DateTimeOffset.UtcNow), new SearchItemRow(range.Id,"b",Path.Combine(_root,"b"),"b","",SearchItemKind.Other,0,DateTimeOffset.UtcNow) });
            var a=Assert.Single(index.Search("a",null)); var b=Assert.Single(index.Search("b",null));
            index.SetPinned(a,true); index.RecordUse(b,DateTimeOffset.UtcNow);
            Assert.Equal("a",index.Search("",null,new SearchQueryOptions(UseRecentHistory:true))[0].Name);
        }
        using (var index=new SearchIndex(db))
        {
            Assert.True(index.Search("",null)[0].IsPinned);
            index.ClearHistory(); Assert.True(index.Search("",null)[0].IsPinned);
            index.DeleteRange(range.Id); Assert.Empty(index.Search("",null));
        }
    }
    public void Dispose() { try { Directory.Delete(_root,true); } catch (IOException) { } }
}
