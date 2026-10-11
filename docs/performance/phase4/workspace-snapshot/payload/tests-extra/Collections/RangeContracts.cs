using Files.App.Utils.Storage;
using System.Collections;
using System.Collections.Specialized;

internal static class RangeContracts
{
    public static IEnumerable<(string Name, Action Test)> Cases()
    {
        yield return ("single_use_normal_add_insert", SingleUseNormal);
        yield return ("single_use_bulk_add_insert", SingleUseBulk);
        yield return ("single_use_grouped_add_insert", SingleUseGrouped);
        yield return ("single_use_replace", SingleUseReplace);
        yield return ("throwing_source_add_is_atomic", () => ThrowingSource(false));
        yield return ("throwing_source_insert_is_atomic", () => ThrowingSource(true));
        yield return ("self_add_event_matches_added_range", () => SelfRange(false));
        yield return ("self_insert_event_matches_added_range", () => SelfRange(true));
        yield return ("deferred_self_source_snapshot", DeferredSelfSource);
        yield return ("empty_single_use_invalid_insert_noop", EmptySingleUse);
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    private static void SingleUseNormal()
    {
        var target = new BulkConcurrentObservableCollection<int>();
        var snapshots = new List<int[]>();
        target.CollectionChanged += (_, e) => snapshots.Add(e.NewItems!.Cast<int>().ToArray());
        target.AddRange(new SingleUse<int>([1, 4]));
        target.InsertRange(1, new SingleUse<int>([2, 3]));
        Require(target.SequenceEqual([1, 2, 3, 4]), "Single-use range data differs.");
        Require(snapshots.Count == 2 && snapshots[0].SequenceEqual([1, 4]) && snapshots[1].SequenceEqual([2, 3]), "Single-use event data differs.");
    }

    private static void SingleUseBulk()
    {
        var target = new BulkConcurrentObservableCollection<int>();
        var events = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => events.Add(e.Action);
        target.BeginBulkOperation();
        target.AddRange(new SingleUse<int>([1, 4]));
        target.InsertRange(1, new SingleUse<int>([2, 3]));
        target.EndBulkOperation();
        Require(target.SequenceEqual([1, 2, 3, 4]), "Single-use bulk data differs.");
        Require(events.SequenceEqual([NotifyCollectionChangedAction.Reset]), "Single-use bulk reset differs.");
    }

    private static void SingleUseGrouped()
    {
        foreach (var bulk in new[] { false, true })
        {
            var target = new BulkConcurrentObservableCollection<int> { ItemGroupKeySelector = x => (x % 2).ToString() };
            if (bulk) target.BeginBulkOperation();
            target.AddRange(new SingleUse<int>([1, 4]));
            target.InsertRange(1, new SingleUse<int>([2, 3]));
            if (bulk) target.EndBulkOperation();
            var groups = target.GroupedCollection ?? throw new InvalidOperationException("Groups missing.");
            Require(groups.Count == 2 && groups.All(x => x.Count == 2), "Grouped single-use membership differs.");
            Require(groups.SelectMany(x => x).Order().SequenceEqual([1, 2, 3, 4]), "Grouped single-use contents differ.");
        }
    }

    private static void SingleUseReplace()
    {
        var target = new BulkConcurrentObservableCollection<int>([1, 2, 3, 4]);
        NotifyCollectionChangedEventArgs? observed = null;
        target.CollectionChanged += (_, e) => observed = e;
        target.ReplaceRange(1, new SingleUse<int>([7, 8]));
        Require(target.SequenceEqual([1, 7, 8, 4]), "Single-use replacement differs.");
        Require(observed!.OldItems!.Cast<int>().SequenceEqual([2, 3]) && observed.NewItems!.Cast<int>().SequenceEqual([7, 8]), "Single-use replacement event differs.");
    }

    private static void ThrowingSource(bool insert)
    {
        var target = new BulkConcurrentObservableCollection<int>([1, 2]);
        var events = 0;
        target.CollectionChanged += (_, _) => events++;
        try
        {
            if (insert) target.InsertRange(1, Throwing());
            else target.AddRange(Throwing());
            throw new InvalidOperationException("Source error was suppressed.");
        }
        catch (IOException) { }
        Require(target.SequenceEqual([1, 2]) && events == 0, "Failed source partially modified collection.");

        static IEnumerable<int> Throwing()
        {
            yield return 8;
            throw new IOException("Synthetic source error.");
        }
    }

    private static void SelfRange(bool insert)
    {
        var target = new BulkConcurrentObservableCollection<int>([1, 2]);
        NotifyCollectionChangedEventArgs? observed = null;
        target.CollectionChanged += (_, e) => observed = e;
        if (insert) target.InsertRange(0, target);
        else target.AddRange(target);
        Require(target.SequenceEqual([1, 2, 1, 2]), "Self range data differs.");
        Require(observed!.NewItems!.Cast<int>().SequenceEqual([1, 2]), "Self range event includes previously stored items.");
    }

    private static void DeferredSelfSource()
    {
        var target = new BulkConcurrentObservableCollection<int>([1, 2, 3]);
        // Bound the old live-enumerator defect to avoid runaway growth in baseline probes.
        target.AddRange(target.Where(x => x % 2 != 0).Take(3));
        Require(target.SequenceEqual([1, 2, 3, 1, 3]), "Deferred self range differs.");
    }

    private static void EmptySingleUse()
    {
        var target = new BulkConcurrentObservableCollection<int>();
        var events = 0;
        target.CollectionChanged += (_, _) => events++;
        target.InsertRange(-1, new SingleUse<int>([]));
        target.BeginBulkOperation();
        target.InsertRange(-1, new SingleUse<int>([]));
        target.EndBulkOperation();
        Require(target.Count == 0 && events == 1, "Empty input changed invalid-index or bulk semantics.");
    }

    private sealed class SingleUse<T>(IEnumerable<T> items) : IEnumerable<T>
    {
        private bool enumerated;
        public IEnumerator<T> GetEnumerator()
        {
            if (enumerated) throw new InvalidOperationException("Sequence enumerated more than once.");
            enumerated = true;
            return items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
