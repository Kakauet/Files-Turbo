using System.Globalization;
using System.Text.Json;

var variant = args.Length > 0 ? args[0] : "phase2";
var datasetRoot = Path.GetFullPath(args.Length > 1 ? args[1] : "benchmarks/synthetic");
var outputRoot = Path.GetFullPath(args.Length > 2 ? args[2] : "benchmarks/results/phase2/navigation");
Directory.CreateDirectory(outputRoot);
var contractOnly = args.Contains("--contracts");
var rows = new List<Measurement>();
if (!contractOnly)
{
    await Measure(Path.Combine(datasetRoot, "1000"), variant, 1000, -1);
    foreach (var count in new[] { 1000, 10000, 50000, 100000 })
        for (var run = 0; run < 5; run++)
            rows.Add(await Measure(Path.Combine(datasetRoot, count.ToString()), variant, count, run));
}
using (var cts = new CancellationTokenSource())
using (var folder = new DirectoryFolder(Path.Combine(datasetRoot, "100000")))
{
    var publications = 0;
    var watch = Stopwatch.StartNew();
    var canceled = false;
    try
    {
        await UniversalStorageEnumerator.ListEntries(folder, new(folder, folder.Path), cts.Token, -1, 32, batch =>
        {
            publications++;
            cts.Cancel();
            return Task.CompletedTask;
        });
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { canceled = true; }
    var cancellation = new { variant, canceled, fetches = folder.Fetches, publications, elapsedMs = watch.Elapsed.TotalMilliseconds, stopsBeforeNextPage = folder.Fetches == 1 };
    File.WriteAllText(Path.Combine(outputRoot, $"{variant}-cancellation.json"), JsonSerializer.Serialize(cancellation, new JsonSerializerOptions { WriteIndented = true }));
    if (variant == "phase2" && (!canceled || folder.Fetches != 1)) throw new Exception("Cancelled enumeration continued fetching pages.");
    Console.WriteLine(JsonSerializer.Serialize(cancellation));
}
if (variant == "phase2")
{
    long baselineRetained = Collect();
    for (var repeat = 0; repeat < 20; repeat++) await Measure(Path.Combine(datasetRoot, "1000"), variant, 1000, repeat);
    long retainedDelta = Collect() - baselineRetained;
    File.WriteAllText(Path.Combine(outputRoot, "phase2-soak.json"), JsonSerializer.Serialize(new { operations = 20, dataset = 1000, retainedDeltaBytes = retainedDelta, scope = "UniversalStorageEnumerator + source publication collections; no WinUI" }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Soak retained managed delta: {retainedDelta} bytes");
}
if (rows.Count > 0)
{
    File.WriteAllText(Path.Combine(outputRoot, $"{variant}-samples.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    using var csv = File.CreateText(Path.Combine(outputRoot, $"{variant}-samples.csv"));
    csv.WriteLine("variant,count,run,firstResultMs,totalMs,allocBytes,publicationItems,publications,maxPublishMs,cpuMs,peakWorkingSetBytes,managedBytesAtEnd,correct");
    foreach (var row in rows)
        csv.WriteLine(string.Join(',', row.Variant, row.Count, row.Run, F(row.FirstResultMs), F(row.TotalMs), row.AllocBytes, row.PublicationItems, row.Publications, F(row.MaxPublishMs), F(row.CpuMs), row.PeakWorkingSetBytes, row.ManagedBytesAtEnd, row.Correct));
}

static string F(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
static long Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
static async Task<Measurement> Measure(string path, string variant, int expectedCount, int run)
{
    using var process = Process.GetCurrentProcess();
    using var folder = new DirectoryFolder(path);
    var logical = new List<ListedItem>();
    var visible = new BulkConcurrentObservableCollection<ListedItem>();
    var allocated = GC.GetTotalAllocatedBytes();
    var cpu = process.TotalProcessorTime;
    var watch = Stopwatch.StartNew();
    var firstResult = -1d;
    var publicationItems = 0L;
    var publications = 0;
    var maxPublish = 0d;
    Task Publish(List<ListedItem> batch, bool final)
    {
        var publishWatch = Stopwatch.StartNew();
        logical.AddRange(batch);
        if (final || logical.Count <= 10_000)
            logical = SortingHelper.OrderFileList(logical, SortOption.Name, SortDirection.Ascending, false, false).ToList();
        // Source ShellViewModel applies a snapshot and clears/repopulates the bulk collection.
        var snapshot = logical.ToList();
        publicationItems += snapshot.Count;
        visible.BeginBulkOperation();
        visible.Clear();
        visible.AddRange(snapshot);
        visible.EndBulkOperation();
        publications++;
        if (firstResult < 0 && visible.Count > 0) firstResult = watch.Elapsed.TotalMilliseconds;
        maxPublish = Math.Max(maxPublish, publishWatch.Elapsed.TotalMilliseconds);
        return Task.CompletedTask;
    }
    var remainder = await UniversalStorageEnumerator.ListEntries(folder, new(folder, path), CancellationToken.None, -1, 32, batch => Publish(batch, false));
    await Publish(remainder, true);
    var elapsed = watch.Elapsed.TotalMilliseconds;
    var correct = visible.Count == expectedCount && logical.Select(x => x.ItemPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == expectedCount && logical.Select(x => x.Name).SequenceEqual(logical.Select(x => x.Name).Order(StringComparer.OrdinalIgnoreCase));
    if (!correct) throw new Exception($"Enumeration loss, duplicate, or order failure: {variant}/{expectedCount}");
    var measurement = new Measurement(variant, expectedCount, run, firstResult, elapsed, GC.GetTotalAllocatedBytes() - allocated, publicationItems, publications, maxPublish, (process.TotalProcessorTime - cpu).TotalMilliseconds, process.PeakWorkingSet64, GC.GetTotalMemory(false), correct);
    if (run >= 0) Console.WriteLine(JsonSerializer.Serialize(measurement));
    return measurement;
}
record Measurement(string Variant, int Count, int Run, double FirstResultMs, double TotalMs, long AllocBytes, long PublicationItems, int Publications, double MaxPublishMs, double CpuMs, long PeakWorkingSetBytes, long ManagedBytesAtEnd, bool Correct);
