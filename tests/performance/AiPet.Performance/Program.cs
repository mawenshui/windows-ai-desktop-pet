using System.Diagnostics;
using System.Text.Json;
using AiPet.Search;

if (args.Length != 2 || !int.TryParse(args[1], out var itemCount) || itemCount < 1)
{
    Console.Error.WriteLine("Usage: AiPet.Performance <report-path> <item-count>");
    return 2;
}

var reportPath = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
var temporaryRoot = Path.Combine(Path.GetTempPath(), "aipet-performance-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryRoot);
try
{
    using var index = new SearchIndex(Path.Combine(temporaryRoot, "index.db"));
    var rangeId = Guid.NewGuid();
    index.UpsertRange(new SearchRange(rangeId, @"C:\AiPetBench\anonymous", SearchRangeState.Ready, null, DateTimeOffset.UtcNow));
    const int batchSize = 1000;
    for (var offset = 0; offset < itemCount; offset += batchSize)
    {
        var count = Math.Min(batchSize, itemCount - offset);
        var rows = Enumerable.Range(offset, count).Select(i => new SearchItemRow(
            rangeId,
            $"anonymous-report-{i:D5}.txt",
            $@"C:\AiPetBench\anonymous\anonymous-report-{i:D5}.txt",
            $"anonymous-report-{i:D5}.txt",
            ".txt",
            SearchItemKind.Document,
            1024 + i,
            DateTimeOffset.UnixEpoch.AddSeconds(i))).ToArray();
        index.InsertItems(rows);
    }

    var options = new SearchQueryOptions(
        EnableWildcardSearch: false,
        EnableRegexSearch: false,
        RangeId: rangeId,
        Limit: 20,
        Offset: 0);
    for (var i = 0; i < 20; i++) _ = index.Search("report-1", SearchItemKind.Document, options);
    var samples = new double[200];
    for (var i = 0; i < samples.Length; i++)
    {
        var query = $"report-{(i * 97) % itemCount:D5}";
        var stopwatch = Stopwatch.StartNew();
        _ = index.Search(query, SearchItemKind.Document, options);
        stopwatch.Stop();
        samples[i] = stopwatch.Elapsed.TotalMilliseconds;
    }
    Array.Sort(samples);
    static double Percentile(double[] values, double percentile) =>
        values[Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1)];

    var report = new
    {
        schemaVersion = 1,
        dataset = new { itemCount, pathPrefix = @"C:\AiPetBench\anonymous", containsRealUserData = false },
        searchFirstPage = new
        {
            iterations = samples.Length,
            p50Ms = Percentile(samples, 0.50),
            p95Ms = Percentile(samples, 0.95),
            maxMs = samples[^1]
        }
    };
    File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
finally
{
    try { Directory.Delete(temporaryRoot, true); } catch { }
}
