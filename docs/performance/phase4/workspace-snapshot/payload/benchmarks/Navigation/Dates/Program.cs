using System.Globalization;
using System.Text.Json;

var variant = args.Length > 0 ? args[0] : "phase2";
var dataset = Path.GetFullPath(args.Length > 1 ? args[1] : "benchmarks/synthetic");
var output = Path.GetFullPath(args.Length > 2 ? args[2] : "benchmarks/results/phase2/dates");
Directory.CreateDirectory(output);
var rows = new List<object>();
var contracts = new List<object>();
var warmupsOption = args.FirstOrDefault(x => x.StartsWith("--warmups=", StringComparison.Ordinal));
var warmups = warmupsOption is null ? 1 : int.Parse(warmupsOption[10..]);
var countOption = args.FirstOrDefault(x => x.StartsWith("--count=", StringComparison.Ordinal));
var counts = countOption is null ? new[] { 1000, 10000, 50000, 100000 } : new[] { int.Parse(countOption[8..]) };
if (!args.Contains("--contracts"))
{
    foreach (var count in counts)
    {
        // Metadata comes from the existing synthetic files; formatter/materialization timing excludes this read.
        var metadata = new DirectoryInfo(Path.Combine(dataset, count.ToString())).EnumerateFiles().Select(x => (Created: (DateTimeOffset)x.CreationTimeUtc, Modified: (DateTimeOffset)x.LastWriteTimeUtc, Accessed: (DateTimeOffset)x.LastAccessTimeUtc)).ToArray();
        foreach (var visible in new[] { 100, count })
        {
            for (var run = -warmups; run < 5; run++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                CountingFormatter.Shared.Calls = 0;
                var allocated = GC.GetTotalAllocatedBytes();
                var gen0 = GC.CollectionCount(0); var gen1 = GC.CollectionCount(1); var gen2 = GC.CollectionCount(2);
                using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
                var watch = Stopwatch.StartNew();
                var items = metadata.Select(x => new ListedItem { ItemDateCreatedReal = x.Created, ItemDateModifiedReal = x.Modified, ItemDateAccessedReal = x.Accessed }).ToArray();
                var materializeMs = watch.Elapsed.TotalMilliseconds;
                foreach (var item in items.Take(visible)) { _ = item.ItemDateCreated; _ = item.ItemDateModified; _ = item.ItemDateAccessed; }
                var totalMs = watch.Elapsed.TotalMilliseconds;
                var formatCalls = CountingFormatter.Shared.Calls;
                var allocatedBytes = GC.GetTotalAllocatedBytes() - allocated;
                if (run >= 0) rows.Add(new { variant, count, visibleRows = visible, run, materializeMs, totalMs, formatCalls, allocatedBytes, retainedManagedBytes = GC.GetTotalMemory(false), gen0Collections = GC.CollectionCount(0) - gen0, gen1Collections = GC.CollectionCount(1) - gen1, gen2Collections = GC.CollectionCount(2) - gen2, cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds });
                GC.KeepAlive(items);
            }
        }
    }
}
if (variant == "phase2")
{
    CountingFormatter.Shared.Formatter = new SystemDateTimeFormatter();
    var value = new DateTimeOffset(2026, 4, 5, 12, 34, 56, TimeSpan.Zero);
    var empty = new ListedItem();
    Assert(empty.ItemDateModified is null && empty.ItemDateCreated is null && empty.ItemDateAccessed is null, "Uninitialized labels changed from original null defaults."); Pass("uninitialized_date_labels_preserve_null_defaults");
    CountingFormatter.Shared.Calls = 0;
    var item = new ListedItem { ItemDateCreatedReal = value, ItemDateModifiedReal = value, ItemDateAccessedReal = value };
    Assert(CountingFormatter.Shared.Calls == 0, "Unseen row formatted labels eagerly."); Pass("unrealized_row_does_not_format_dates");
    var expected = new SystemDateTimeFormatter().ToShortLabel(value);
    Assert(item.ItemDateModified == expected && item.ItemDateModified == expected && CountingFormatter.Shared.Calls == 1, "First read or cached label was wrong."); Pass("first_read_matches_original_formatter_and_is_cached");
    var changes = new List<string?>(); item.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
    item.ItemDateModifiedReal = value.AddDays(1);
    Assert(changes.Contains(nameof(ListedItem.ItemDateModified)) && item.ItemDateModified == new SystemDateTimeFormatter().ToShortLabel(value.AddDays(1)), "Metadata update did not invalidate binding label."); Pass("metadata_change_invalidates_observed_label_and_notifies_binding");
    changes.Clear();
    item.ItemDateModifiedReal = item.ItemDateModifiedReal;
    Assert(changes.Count == 0, "Unchanged metadata emitted redundant binding notifications."); Pass("unchanged_real_metadata_keeps_cached_label_without_notification");
    CountingFormatter.Shared.Formatter = new UniversalDateTimeFormatter();
    Assert(item.IsRealChanges, "Formatter change was missed for cached label."); item.UpdateReal();
    Assert(item.ItemDateModified == new UniversalDateTimeFormatter().ToShortLabel(value.AddDays(1)), "Formatter refresh did not update observed label."); Pass("formatter_change_refreshes_observed_label");
    var unobserved = new ListedItem { ItemDateModifiedReal = value };
    CountingFormatter.Shared.Calls = 0;
    Assert(!unobserved.IsRealChanges, "Unseen row was materialized during time refresh."); unobserved.UpdateReal();
    Assert(CountingFormatter.Shared.Calls == 0, "Unseen row formatted during clock refresh."); Pass("clock_refresh_skips_unobserved_labels");
    Assert(unobserved.ItemDateModified == new UniversalDateTimeFormatter().ToShortLabel(value), "Unseen label used old formatter on first read."); Pass("first_read_after_formatter_change_uses_current_format");
    foreach (var edge in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero), value.ToOffset(TimeSpan.FromHours(5.5)) })
    {
        var edgeItem = new ListedItem { ItemDateModifiedReal = edge };
        Assert(edgeItem.ItemDateModified == new UniversalDateTimeFormatter().ToShortLabel(edge), "Invalid date/timezone differs from original.");
        Assert(edgeItem.ItemDateModifiedReal == edge, "Real sorting date changed.");
    }
    Pass("edge_dates_timezone_and_real_sort_values_preserved");
    CountingFormatter.Shared.Formatter = new ApplicationDateTimeFormatter();
    var recent = new ListedItem { ItemDateModifiedReal = DateTimeOffset.Now.AddSeconds(-2) };
    _ = recent.ItemDateModified;
    recent.ItemDateModifiedReal = DateTimeOffset.Now.AddMinutes(-5);
    Assert(recent.ItemDateModified == CountingFormatter.Shared.Formatter.ToShortLabel(recent.ItemDateModifiedReal), "Relative label update failed."); Pass("relative_application_label_updates_after_metadata_change");
}
File.WriteAllText(Path.Combine(output, $"{variant}-samples.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
if (rows.Count > 0)
{
    using var csv = File.CreateText(Path.Combine(output, $"{variant}-samples.csv"));
    csv.WriteLine("variant,count,visibleRows,run,materializeMs,totalMs,formatCalls,allocatedBytes,retainedManagedBytes,gen0Collections,gen1Collections,gen2Collections,cpuMs");
    foreach (var row in rows)
    {
        var element = JsonSerializer.SerializeToElement(row);
        csv.WriteLine(string.Join(',', element.EnumerateObject().Select(property => property.Value.ToString())));
    }
}
File.WriteAllText(Path.Combine(output, $"{variant}-contracts.json"), JsonSerializer.Serialize(contracts, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{rows.Count} samples, {contracts.Count} contracts; source date properties and source System/Universal/Application formatters.");
void Pass(string name) => contracts.Add(new { name, passed = true });
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
