using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiPet.Search;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class ContentSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aipet-content-search-" + Guid.NewGuid().ToString("N"));

    public ContentSearchTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Opt_in_indexes_only_bounded_decodable_text_and_explains_matches()
    {
        var files = Directory.CreateDirectory(Path.Combine(_root, "files")).FullName;
        File.WriteAllText(Path.Combine(files, "alpha.txt"), "first line\nanonymous needle in body", new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(files, "bravo.md"), "UTF16 NEEDLE body", Encoding.Unicode);
        File.WriteAllBytes(Path.Combine(files, "invalid.txt"), [0xC3, 0x28]);
        File.WriteAllText(Path.Combine(files, "binary.txt"), "prefix\0needle");
        File.WriteAllText(Path.Combine(files, "large.txt"), new string('x', ContentSearchPolicy.MaximumFileBytes + 1));
        File.WriteAllText(Path.Combine(files, "ignored.pdf"), "needle");

        using var search = NewSearch();
        search.AddRange(files);
        var range = Assert.Single(search.ListRanges());
        await search.IndexRangeAsync(range.Id);
        Assert.Empty(search.Search("needle", null, new SearchQueryOptions(EnableContentSearch: true)));

        search.SetContentSearchEnabled(true);
        await search.IndexRangeAsync(range.Id);

        var matches = search.Search("needle", null, new SearchQueryOptions(EnableContentSearch: true));
        Assert.Equal(2, matches.Count);
        Assert.All(matches, match => Assert.Equal("正文连续包含", match.MatchReason));
        Assert.All(matches, match => Assert.InRange(match.MatchSnippet.Length, 1, ContentSearchPolicy.MaximumSnippetCharacters));
        Assert.DoesNotContain('\n', matches[0].MatchSnippet);
        var summary = search.GetContentSummary(range.Id);
        Assert.Equal(2, summary.Indexed);
        Assert.Equal(4, summary.Skipped);
        Assert.True(summary.BytesRead > 0);
    }

    [Fact]
    public async Task Metadata_matches_rank_before_content_and_patterns_never_read_body()
    {
        var files = Directory.CreateDirectory(Path.Combine(_root, "ranking")).FullName;
        File.WriteAllText(Path.Combine(files, "needle-title.txt"), "ordinary body");
        File.WriteAllText(Path.Combine(files, "other.txt"), "needle only in body");
        using var search = NewSearch();
        search.SetContentSearchEnabled(true);
        search.AddRange(files);
        await search.IndexRangeAsync(Assert.Single(search.ListRanges()).Id);

        var literal = search.Search("needle", null, new SearchQueryOptions(EnableContentSearch: true));
        Assert.Equal("needle-title.txt", literal[0].Name);
        Assert.Equal("名称前缀匹配", literal[0].MatchReason);
        Assert.Equal("other.txt", literal[1].Name);
        Assert.Equal("正文连续包含", literal[1].MatchReason);

        Assert.DoesNotContain(search.Search("needle*", null, new SearchQueryOptions(
            EnableWildcardSearch: true,
            EnableContentSearch: true)), item => item.Name == "other.txt");
        Assert.DoesNotContain(search.Search("re:needle", null, new SearchQueryOptions(
            EnableRegexSearch: true,
            EnableContentSearch: true)), item => item.Name == "other.txt");
    }

    [Fact]
    public async Task Disabling_or_removing_a_range_purges_derived_content_immediately()
    {
        var first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        File.WriteAllText(Path.Combine(first, "first.txt"), "first secret marker");
        File.WriteAllText(Path.Combine(second, "second.txt"), "second secret marker");
        using var search = NewSearch();
        search.SetContentSearchEnabled(true);
        search.AddRange(first);
        search.AddRange(second);
        foreach (var range in search.ListRanges()) await search.IndexRangeAsync(range.Id);
        Assert.Equal(2, search.GetContentSummary().Indexed);

        var removed = search.ListRanges().Single(range => range.Path == first);
        search.RemoveRange(removed.Id);
        Assert.Equal(1, search.GetContentSummary().Indexed);
        Assert.Empty(search.Search("first secret", null, new SearchQueryOptions(EnableContentSearch: true)));

        search.SetContentSearchEnabled(false);
        Assert.Equal(0, search.GetContentSummary().Indexed);
        Assert.Empty(search.Search("second secret", null, new SearchQueryOptions(EnableContentSearch: true)));
    }

    [Fact]
    public async Task Disabling_during_rebuild_prevents_stale_content_from_returning()
    {
        var files = Directory.CreateDirectory(Path.Combine(_root, "revocation")).FullName;
        var path = Path.Combine(files, "note.txt");
        File.WriteAllText(path, "revoked needle");
        using var reached = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        IEnumerable<SearchItemRow> Scan(SearchRange range)
        {
            reached.Set();
            resume.Wait(TimeSpan.FromSeconds(5));
            yield return Row(range, path);
        }

        using var search = new SearchService(Path.Combine(_root, "revocation-state", "index.db"), Scan, () => []);
        search.SetContentSearchEnabled(true);
        search.AddRange(files);
        var task = search.IndexRangeAsync(Assert.Single(search.ListRanges()).Id);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(5)));
        search.SetContentSearchEnabled(false);
        resume.Set();
        await task;

        Assert.Equal(0, search.GetContentSummary().Indexed);
        Assert.Empty(search.Search("revoked needle", null, new SearchQueryOptions(EnableContentSearch: true)));
    }

    [Fact]
    public async Task Watcher_refreshes_changed_content_without_reauthorising_the_range()
    {
        var files = Directory.CreateDirectory(Path.Combine(_root, "watcher")).FullName;
        var path = Path.Combine(files, "note.txt");
        File.WriteAllText(path, "old marker");
        File.WriteAllText(Path.Combine(files, "unsupported.pdf"), "not indexed");
        using var search = NewSearch();
        search.SetContentSearchEnabled(true);
        search.AddRange(files);
        await search.IndexRangeAsync(Assert.Single(search.ListRanges()).Id);
        Assert.Single(search.Search("old marker", null, new SearchQueryOptions(EnableContentSearch: true)));

        File.WriteAllText(path, "new marker");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (search.Search("new marker", null, new SearchQueryOptions(EnableContentSearch: true)).Count == 1
                && search.Search("old marker", null, new SearchQueryOptions(EnableContentSearch: true)).Count == 0)
            {
                Assert.Equal(1, search.GetContentSummary().Skipped);
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail("正文文件变更后，监视器未在时限内刷新派生索引。");
    }

    [Fact]
    public async Task Per_range_file_and_corpus_budgets_are_enforced()
    {
        var countRoot = Directory.CreateDirectory(Path.Combine(_root, "count-budget")).FullName;
        for (var index = 0; index <= ContentSearchPolicy.MaximumFilesPerRange; index++)
            File.WriteAllText(Path.Combine(countRoot, $"{index:D4}.txt"), "x");
        using (var search = NewSearch("count-state"))
        {
            search.SetContentSearchEnabled(true);
            search.AddRange(countRoot);
            await search.IndexRangeAsync(Assert.Single(search.ListRanges()).Id);
            Assert.Equal(ContentSearchPolicy.MaximumFilesPerRange, search.GetContentSummary().Indexed);
        }

        var corpusRoot = Directory.CreateDirectory(Path.Combine(_root, "corpus-budget")).FullName;
        var body = new string('x', ContentSearchPolicy.MaximumFileBytes);
        var expectedFiles = ContentSearchPolicy.MaximumCorpusBytesPerRange / ContentSearchPolicy.MaximumFileBytes;
        for (var index = 0; index <= expectedFiles; index++)
            File.WriteAllText(Path.Combine(corpusRoot, $"{index:D3}.txt"), body, new UTF8Encoding(false));
        using var corpusSearch = NewSearch("corpus-state");
        corpusSearch.SetContentSearchEnabled(true);
        corpusSearch.AddRange(corpusRoot);
        await corpusSearch.IndexRangeAsync(Assert.Single(corpusSearch.ListRanges()).Id);
        var summary = corpusSearch.GetContentSummary();
        Assert.Equal(expectedFiles, summary.Indexed);
        Assert.Equal(ContentSearchPolicy.MaximumCorpusBytesPerRange, summary.BytesRead);
        Assert.True(summary.Skipped >= 1);
    }

    private SearchService NewSearch(string state = "state") =>
        new(Path.Combine(_root, state, "index.db"), appProvider: () => []);

    private static SearchItemRow Row(SearchRange range, string path)
    {
        var info = new FileInfo(path);
        return new SearchItemRow(
            range.Id,
            info.Name,
            info.FullName,
            Path.GetRelativePath(range.Path, info.FullName),
            info.Extension,
            SearchItemKind.Document,
            info.Length,
            info.LastWriteTimeUtc);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
