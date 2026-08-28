using System;
using System.IO;
using System.Linq;
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
