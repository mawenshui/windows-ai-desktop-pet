using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiPet.Search;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class SearchServiceTests : IDisposable
{
    private readonly string _root;
    private readonly SearchService _search;

    public SearchServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aipet-search-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _search = new SearchService(Path.Combine(_root, "state", "index.db"), appProvider: () => []);
    }

    public void Dispose()
    {
        _search.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task Recursively_indexes_nested_names_and_does_not_search_parent_path()
    {
        var rangeRoot = MakeRange("alpha");
        var nested = Path.Combine(rangeRoot, "secret-parent");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "budget-final.xlsx"), "fixture");
        var range = await AddAndIndex(rangeRoot);

        Assert.Single(_search.Search("budget", null));
        Assert.Empty(_search.Search("secret-parent", SearchItemKind.Document));
        Assert.True(_search.CountItems(range.Id) >= 2);
    }

    [Fact]
    public async Task Wildcards_are_opt_in_and_apply_to_filename_only()
    {
        var rangeRoot = MakeRange("wildcard");
        File.WriteAllText(Path.Combine(rangeRoot, "budget-2026.xlsx"), "fixture");
        await AddAndIndex(rangeRoot);

        Assert.Empty(_search.Search("budget-*.xlsx", null));
        var rows = _search.Search("budget-*.xlsx", null, new SearchQueryOptions(EnableWildcardSearch: true));
        Assert.Equal("budget-2026.xlsx", Assert.Single(rows).Name);
    }

    [Fact]
    public async Task Regex_is_opt_in_validated_and_timeout_bounded()
    {
        var rangeRoot = MakeRange("regex");
        File.WriteAllText(Path.Combine(rangeRoot, "report-42.txt"), "fixture");
        File.WriteAllText(Path.Combine(rangeRoot, "report-final.txt"), "fixture");
        await AddAndIndex(rangeRoot);

        Assert.Empty(_search.Search(@"re:^report-\d+\.txt$", null));
        var rows = _search.Search(
            @"re:^report-\d+\.txt$",
            null,
            new SearchQueryOptions(EnableRegexSearch: true));
        Assert.Equal("report-42.txt", Assert.Single(rows).Name);
        Assert.Throws<SearchQueryException>(() => _search.Search(
            "re:[",
            null,
            new SearchQueryOptions(EnableRegexSearch: true)));
        Assert.Throws<SearchQueryException>(() => _search.Search(
            "re:",
            null,
            new SearchQueryOptions(EnableRegexSearch: true)));
    }

    [Fact]
    public async Task Scope_filter_and_literal_sql_wildcards_are_respected()
    {
        var firstRoot = MakeRange("first");
        var secondRoot = MakeRange("second");
        File.WriteAllText(Path.Combine(firstRoot, "100%_ready.txt"), "fixture");
        File.WriteAllText(Path.Combine(secondRoot, "100XXready.txt"), "fixture");
        var first = await AddAndIndex(firstRoot);
        await AddAndIndex(secondRoot);

        var rows = _search.Search(
            "%_",
            null,
            new SearchQueryOptions(RangeId: first.Id));
        Assert.Equal("100%_ready.txt", Assert.Single(rows).Name);
        Assert.All(rows, row => Assert.Equal(first.Id, row.RangeId));
    }

    [Fact]
    public async Task Failed_reindex_keeps_previous_ready_items_and_uses_stable_error_code()
    {
        var dbPath = Path.Combine(_root, "failure-state", "index.db");
        var fail = false;
        IEnumerable<SearchItemRow> Scan(SearchRange range)
        {
            yield return Row(range, fail ? "partial-new.txt" : "previous-ready.txt");
            if (fail) throw new IOException("sensitive machine path must not escape");
        }

        using var search = new SearchService(dbPath, Scan, appProvider: () => []);
        var path = MakeRange("failure-state");
        search.AddRange(path);
        var range = search.ListRanges().Single();
        await search.IndexRangeAsync(range.Id, batchSize: 1);

        fail = true;
        await Assert.ThrowsAsync<IOException>(() => search.IndexRangeAsync(range.Id, batchSize: 1));

        Assert.Single(search.Search("previous-ready", null));
        Assert.Empty(search.Search("partial-new", null));
        var failed = search.GetRange(range.Id)!;
        Assert.Equal(SearchRangeState.Failed, failed.State);
        Assert.Equal("io_error", failed.LastError);
        Assert.DoesNotContain("sensitive", failed.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelled_streaming_reindex_stops_enumeration_and_keeps_previous_items()
    {
        var dbPath = Path.Combine(_root, "cancel-state", "index.db");
        var reindex = false;
        var enumerated = 0;
        IEnumerable<SearchItemRow> Scan(SearchRange range)
        {
            if (!reindex)
            {
                yield return Row(range, "previous-ready.txt");
                yield break;
            }
            for (var i = 0; i < 100; i++)
            {
                enumerated++;
                yield return Row(range, $"new-{i}.txt");
            }
        }

        using var search = new SearchService(dbPath, Scan, appProvider: () => []);
        var path = MakeRange("cancel-state");
        search.AddRange(path);
        var range = search.ListRanges().Single();
        await search.IndexRangeAsync(range.Id, batchSize: 1);

        reindex = true;
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<int>(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            search.IndexRangeAsync(range.Id, progress, batchSize: 1, cancellation.Token));

        Assert.True(enumerated < 100);
        Assert.Single(search.Search("previous-ready", null));
        Assert.Empty(search.Search("new-", null));
        Assert.Equal(SearchRangeState.Cancelled, search.GetRange(range.Id)!.State);
    }

    [Fact]
    public void Deleting_range_also_removes_its_staged_items()
    {
        var dbPath = Path.Combine(_root, "delete-staged-state", "index.db");
        using var index = new SearchIndex(dbPath);
        var range = SearchRange.For(MakeRange("delete-staged-state"));
        index.UpsertRange(range);
        index.PrepareStagedItems(range.Id);
        index.InsertStagedItems([Row(range, "staged-only.txt")]);

        index.DeleteRange(range.Id);
        index.CommitStagedItems(range.Id);

        Assert.Null(index.GetRange(range.Id));
        Assert.Equal(0, index.CountItemsInRange(range.Id));
    }

    private static SearchItemRow Row(SearchRange range, string name) => new(
        range.Id,
        name,
        Path.Combine(range.Path, name),
        name,
        Path.GetExtension(name),
        SearchItemKind.Document,
        1,
        DateTimeOffset.UtcNow);

    private string MakeRange(string name)
    {
        var path = Path.Combine(_root, "ranges", name);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<SearchRange> AddAndIndex(string path)
    {
        _search.AddRange(path);
        var range = _search.ListRanges().Single(x => x.Path == path);
        await _search.IndexRangeAsync(range.Id);
        return range;
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
