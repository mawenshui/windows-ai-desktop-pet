using System;

namespace AiPet.Search;

public enum SearchMatchMode
{
    Literal,
    Wildcard,
    Regex,
}

/// <summary>Optional filename-query capabilities selected by the user.</summary>
public sealed record SearchQueryOptions(
    bool EnableWildcardSearch = false,
    bool EnableRegexSearch = false,
    Guid? RangeId = null,
    int Limit = 100,
    int Offset = 0);

public sealed class SearchQueryException : Exception
{
    public SearchQueryException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
