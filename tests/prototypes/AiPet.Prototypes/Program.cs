using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AiPet.Prototypes;
using AiPet.Search;

if (args.Length!=2) throw new ArgumentException("Usage: prototype-runner <isolated-output-directory> <builtin-pack-directory>");
var output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var fixture=Path.Combine(output,"fixtures-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
var stopwatch=Stopwatch.StartNew();
var searchMeasurements=new List<object>();
foreach (var size in new[] {20000,100000})
{
    using var index=new SearchIndex(Path.Combine(fixture,$"metadata-{size}.db"));
    var range=Guid.NewGuid();
    var rows=Enumerable.Range(0,size).Select(i=>new SearchItemRow(range,$"fixture-{i:D6}.txt",Path.Combine(fixture,$"fixture-{i:D6}.txt"),$"fixture-{i:D6}.txt",".txt",SearchItemKind.Document,32,DateTimeOffset.UnixEpoch)).ToArray();
    stopwatch.Restart(); index.InsertItems(rows); var buildMs=stopwatch.Elapsed.TotalMilliseconds;
    var timings=new List<double>();
    for(var attempt=0;attempt<20;attempt++) { stopwatch.Restart(); if(index.Search("fixture",null,50).Count!=50) throw new Exception("Search sample mismatch."); timings.Add(stopwatch.Elapsed.TotalMilliseconds); }
    searchMeasurements.Add(new {rows=size,buildMs,firstPageP95Ms=timings.OrderBy(value=>value).ElementAt(18)});
}
stopwatch.Restart(); var pack=StrictPetPack.Preview(args[1]); var packMs=stopwatch.Elapsed.TotalMilliseconds;
var report=new {schemaVersion=1,generatedAtUtc=DateTimeOffset.UtcNow,environment=new {os=Environment.OSVersion.VersionString,framework=Environment.Version.ToString(),processors=Environment.ProcessorCount},
    experimentalOnly=true,metadata=searchMeasurements,
    pack=new {pack.Id,pack.Frames,pack.DecodedBytes,pack.License,pack.Fingerprint,previewMs=packMs},limits=new {StrictPetPack.MaximumDecodedBytes}};
var json=JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}); File.WriteAllText(Path.Combine(output,"prototype-results.json"),json); Console.WriteLine(json);
