using Files.App.Utils.Storage;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json;

#if BASELINE
const string variant = "baseline";
#elif PHASE1
const string variant = "phase1";
#else
const string variant = "phase2";
#endif

if (args.Length < 2 || args[0] is not ("--tests" or "--benchmark" or "--contracts"))
    throw new ArgumentException("Usage: Collections --tests|--contracts|--benchmark <workspace output file> [--scenario <name>]");

string[] scenarios = ["bulk_replace_list", "bulk_insert_list", "normal_add_list", "normal_add_iterator", "bulk_add_iterator", "normal_insert_list", "normal_replace_iterator"];
if (args.Length > 2)
{
    if (args[0] != "--benchmark" || args.Length != 4 || args[2] != "--scenario" || !scenarios.Contains(args[3], StringComparer.Ordinal))
        throw new ArgumentException("Unknown benchmark scenario or argument.");
    scenarios = [args[3]];
}

if (args[0] is "--tests" or "--contracts")
{
    var results = new List<TestResult>();
    if (args[0] == "--tests")
    {
        Run("normal_add_snapshot_and_notifications", NormalAdd);
        Run("normal_insert_snapshot_and_notifications", NormalInsert);
        Run("empty_range_has_no_notifications", EmptyRanges);
        Run("empty_invalid_index_bulk_is_noop", EmptyInvalidBulkInsert);
        Run("bulk_add_insert_reset_once", BulkRanges);
        Run("grouped_bulk_add_insert_membership", GroupedRanges);
        Run("grouped_normal_add_insert_membership", GroupedNormalRanges);
        Run("replace_order_preserves_contents", ReplaceAndOrder);
        Run("bulk_large_contents", LargeBulk);
        Run("parallel_bulk_add_preserves_items", ParallelBulk);
        Run("range_enumeration_count", RangeEnumeration);
    }
#if BASELINE || PHASE1
    if (args[0] == "--contracts")
#endif
        foreach (var test in RangeContracts.Cases()) Run(test.Name, test.Test);
    File.WriteAllText(args[1], JsonSerializer.Serialize(new { variant, results }, new JsonSerializerOptions { WriteIndented = true }));
    foreach (var result in results)
        Console.WriteLine($"{result.Name}: {(result.Passed ? "PASS" : "FAIL")} {result.Detail}");
    return results.All(x => x.Passed) ? 0 : 1;

    void Run(string name, Action test)
    {
        try { test(); results.Add(new(name, true, "")); }
        catch (Exception error) { results.Add(new(name, false, error.Message)); }
    }
}

const int repetitions = 21;
using (var writer = new StreamWriter(args[1]))
{
    writer.WriteLine("variant,scenario,count,repetition,iterations,cache,elapsed_ms,allocated_bytes,gen0,gen1,gen2,event_count");
    foreach (var count in new[] { 1_000, 10_000, 50_000, 100_000 })
    {
        var items = Enumerable.Range(0, count).Select(x => new Entry(x)).ToList();
        foreach (var scenario in scenarios)
        {
            var collection = new BulkConcurrentObservableCollection<Entry>();
            if (scenario == "normal_replace_iterator") collection.AddRange(items);
            var events = 0;
            collection.CollectionChanged += (_, _) => events++;
            var iterations = Math.Max(32, 4_000_000 / count);
            for (var warmup = 0; warmup < 8; warmup++)
                Invoke();
            for (var repetition = 0; repetition < repetitions; repetition++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                events = 0;
                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);
                var allocation = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                for (var iteration = 0; iteration < iterations; iteration++)
                    Invoke();
                timer.Stop();
                allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
                if (collection.Count != count || collection[0].Id != 0 || collection[count - 1].Id != count - 1)
                    throw new InvalidOperationException("Benchmark content regression.");
                var expectedEvents = iterations * (scenario.StartsWith("bulk_", StringComparison.Ordinal) || scenario == "normal_replace_iterator" ? 1 : 2);
                if (events != expectedEvents) throw new InvalidOperationException("Benchmark event regression.");
                writer.WriteLine(string.Join(',', variant, scenario, count, repetition, iterations, "warm-component",
                    timer.Elapsed.TotalMilliseconds.ToString("F6", CultureInfo.InvariantCulture), allocation,
                    GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, events));
                writer.Flush();
            }

            void Invoke()
            {
                if (scenario.StartsWith("bulk_", StringComparison.Ordinal))
                    collection.BeginBulkOperation();
                if (scenario != "normal_replace_iterator") collection.Clear();
                switch (scenario)
                {
                    case "bulk_insert_list": collection.InsertRange(0, items); break;
                    case "normal_insert_list": collection.InsertRange(0, items); break;
                    case "normal_replace_iterator": collection.ReplaceRange(0, items.Select(x => x)); break;
                    case "normal_add_iterator":
                    case "bulk_add_iterator": collection.AddRange(items.Select(x => x)); break;
                    default: collection.AddRange(items); break;
                }
                if (scenario.StartsWith("bulk_", StringComparison.Ordinal))
                    collection.EndBulkOperation();
            }
        }
    }
}
Console.WriteLine($"{variant}: benchmark CSV written to {args[1]}");
return 0;

static void Require(bool condition, string detail)
{
    if (!condition) throw new InvalidOperationException(detail);
}

static void NormalAdd()
{
    var collection = new BulkConcurrentObservableCollection<int>();
    var source = new List<int> { 1, 2, 3 };
    var events = new List<NotifyCollectionChangedEventArgs>();
    var properties = new List<string?>();
    collection.CollectionChanged += (_, e) => events.Add(e);
    collection.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
    collection.AddRange(source);
    source[0] = 9;
    Require(collection.SequenceEqual([1, 2, 3]), "Data differs from source snapshot.");
    Require(events.Count == 1 && events[0].Action == NotifyCollectionChangedAction.Add && events[0].NewStartingIndex == 0, "Add event mismatch.");
    Require(events[0].NewItems!.Cast<int>().SequenceEqual([1, 2, 3]), "Event snapshot mutated.");
    Require(properties.SequenceEqual(["Count", "Item[]"]), "Property event mismatch.");
}

static void NormalInsert()
{
    var collection = new BulkConcurrentObservableCollection<int>([1, 4]);
    NotifyCollectionChangedEventArgs? observed = null;
    collection.CollectionChanged += (_, e) => observed = e;
    var source = new List<int> { 2, 3 };
    collection.InsertRange(1, source);
    source.Clear();
    Require(collection.SequenceEqual([1, 2, 3, 4]), "Inserted data mismatch.");
    Require(observed!.NewStartingIndex == 1 && observed.NewItems!.Cast<int>().SequenceEqual([2, 3]), "Insert event mismatch.");
}

static void EmptyRanges()
{
    var collection = new BulkConcurrentObservableCollection<int>();
    var events = 0;
    collection.CollectionChanged += (_, _) => events++;
    collection.AddRange(Array.Empty<int>());
    collection.InsertRange(0, Enumerable.Empty<int>());
    Require(events == 0 && collection.Count == 0, "Empty range changed collection.");
}

static void BulkRanges()
{
    var collection = new BulkConcurrentObservableCollection<int>();
    var events = new List<NotifyCollectionChangedEventArgs>();
    collection.CollectionChanged += (_, e) => events.Add(e);
    collection.BeginBulkOperation();
    collection.AddRange([1, 4]);
    collection.InsertRange(1, [2, 3]);
    Require(events.Count == 0, "Bulk operation emitted interim event.");
    collection.EndBulkOperation();
    Require(collection.SequenceEqual([1, 2, 3, 4]), "Bulk contents mismatch.");
    Require(events.Count == 1 && events[0].Action == NotifyCollectionChangedAction.Reset, "Bulk final reset mismatch.");
}

static void EmptyInvalidBulkInsert()
{
    var collection = new BulkConcurrentObservableCollection<int>();
    collection.InsertRange(-1, Array.Empty<int>());
    collection.BeginBulkOperation();
    collection.InsertRange(-1, Array.Empty<int>());
    collection.InsertRange(-1, Enumerable.Empty<int>().Select(x => x));
    collection.EndBulkOperation();
    Require(collection.Count == 0, "Empty invalid index inserted data.");
}

static void GroupedRanges() => CheckGrouped(true);
static void GroupedNormalRanges() => CheckGrouped(false);

static void CheckGrouped(bool bulk)
{
    var collection = new BulkConcurrentObservableCollection<Entry> { ItemGroupKeySelector = e => (e.Id % 2).ToString(CultureInfo.InvariantCulture) };
    if (bulk) collection.BeginBulkOperation();
    collection.AddRange([new Entry(0), new Entry(3)]);
    collection.InsertRange(1, [new Entry(1), new Entry(2)]);
    if (bulk) collection.EndBulkOperation();
    Require(collection.Select(x => x.Id).SequenceEqual([0, 1, 2, 3]), "Grouped master contents mismatch.");
    Require(collection.GroupedCollection!.Count == 2, "Group count mismatch.");
    foreach (var group in collection.GroupedCollection)
    {
        Require(group.Count == 2, "Group members mismatch.");
        Require(group.All(x => x.Key == group.Model.Key), "Group recorded key mismatch.");
    }
    collection.BeginBulkOperation();
    collection.Clear();
    collection.AddRange([new Entry(4)]);
    collection.EndBulkOperation();
    Require(collection.GroupedCollection.Count == 1 && collection.GroupedCollection[0].Single().Id == 4, "Grouped replace mismatch.");
}

static void ReplaceAndOrder()
{
    var collection = new BulkConcurrentObservableCollection<int>([4, 3, 2, 1]);
    collection.Order(x => x.OrderBy(y => y));
    Require(collection.SequenceEqual([1, 2, 3, 4]), "Ordering changed contents.");
    collection.ReplaceRange(1, [8, 9]);
    Require(collection.SequenceEqual([1, 8, 9, 4]), "Replacement mismatch.");
}

static void LargeBulk()
{
    var collection = new BulkConcurrentObservableCollection<int>();
    collection.BeginBulkOperation();
    collection.AddRange(Enumerable.Range(0, 100_000));
    collection.EndBulkOperation();
    Require(collection.SequenceEqual(Enumerable.Range(0, 100_000)), "Large bulk contents mismatch.");
}

static void ParallelBulk()
{
    var collection = new BulkConcurrentObservableCollection<int>();
    var events = 0;
    collection.CollectionChanged += (_, _) => Interlocked.Increment(ref events);
    collection.BeginBulkOperation();
    Parallel.For(0, 8, worker => collection.AddRange(Enumerable.Range(worker * 1000, 1000)));
    collection.EndBulkOperation();
    Require(collection.Order().SequenceEqual(Enumerable.Range(0, 8000)), "Concurrent bulk lost items.");
    Require(events == 1, "Concurrent bulk reset mismatch.");
}

static void RangeEnumeration()
{
    var collection = new BulkConcurrentObservableCollection<int>();
    var sequence = new CountingSequence<int>([1, 2, 3]);
    collection.AddRange(sequence);
    Require(collection.SequenceEqual([1, 2, 3]), "Iterator contents mismatch.");
#if BASELINE || PHASE1
    Require(sequence.Enumerations == 3, "Old normal enumeration count changed.");
#else
    Require(sequence.Enumerations == 1, "Deferred input enumerated more than once.");
#endif
    collection.BeginBulkOperation();
    var bulkAdd = new CountingSequence<int>([4, 5]);
    var bulkInsert = new CountingSequence<int>([6, 7]);
    collection.AddRange(bulkAdd);
    collection.InsertRange(0, bulkInsert);
    collection.EndBulkOperation();
    Require(collection.SequenceEqual([6, 7, 1, 2, 3, 4, 5]), "Bulk iterator contents mismatch.");
#if BASELINE
    Require(bulkAdd.Enumerations == 3 && bulkInsert.Enumerations == 3, "Unexpected baseline bulk enumeration count.");
#elif PHASE1
    Require(bulkAdd.Enumerations == 2 && bulkInsert.Enumerations == 2, "Bulk path created a redundant event snapshot.");
#else
    Require(bulkAdd.Enumerations == 1 && bulkInsert.Enumerations == 1, "Deferred bulk input enumerated more than once.");
#endif
}

sealed class Entry(int id) : IGroupableItem
{
    public int Id { get; } = id;
    public string? Key { get; set; }
}

sealed record TestResult(string Name, bool Passed, string Detail);

sealed class CountingSequence<T>(IEnumerable<T> values) : IEnumerable<T>
{
    public int Enumerations { get; private set; }
    public IEnumerator<T> GetEnumerator()
    {
        Enumerations++;
        return values.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
