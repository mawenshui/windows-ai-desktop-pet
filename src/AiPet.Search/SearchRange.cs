using System;
using System.Collections.Generic;

namespace AiPet.Search;

public enum SearchRangeState
{
    NotConfigured = 0,
    Preparing     = 1,
    Ready         = 2,
    Failed        = 3,
}

public sealed record SearchRange(
    Guid Id,
    string Path,
    SearchRangeState State,
    string? LastError,
    DateTimeOffset? LastIndexedAt)
{
    public static SearchRange For(string path) =>
        new(Guid.NewGuid(), path, SearchRangeState.NotConfigured, null, null);
}

public sealed record SearchRangeSummary(
    Guid Id,
    string Path,
    SearchRangeState State,
    int Items)
{
    public static SearchRangeSummary From(SearchRange r, int items) =>
        new(r.Id, r.Path, r.State, items);
}
