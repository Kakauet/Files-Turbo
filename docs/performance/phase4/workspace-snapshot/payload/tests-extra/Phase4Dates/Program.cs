using Files.App.ViewModels;
using Files.App.Views.Layouts;
using System.Text.Json;

var results = new List<object>();
Run("explicit_rows_refresh_cached_labels_without_touching_hidden_rows", () =>
{
    var visible = Create(); var hidden = Create();
    var oldVisible = visible.ItemDateModified; var oldHidden = hidden.ItemDateModified;
    var shell = new ShellViewModel([visible, hidden]);
    ListedItem.Formatter.Prefix = "changed:";
    shell.UpdateDateDisplay([visible]);
    Assert(visible.ItemDateModified != oldVisible && hidden.ItemDateModified == oldHidden, "Explicit rows refreshed hidden items or left visible text stale.");
    shell.UpdateDateDisplay();
    Assert(hidden.ItemDateModified != oldHidden, "Default whole-folder refresh lost setting-change semantics.");
    ListedItem.Formatter.Prefix = "";
});
Run("empty_visible_collection_does_not_fall_back_to_full_folder", () =>
{
    var hidden = Create(); _ = hidden.ItemDateModified;
    var shell = new ShellViewModel([hidden]);
    var calls = ListedItem.Formatter.Calls;
    shell.UpdateDateDisplay([]);
    Assert(ListedItem.Formatter.Calls == calls, "Empty explicit viewport refreshed the whole folder.");
});
Run("unread_date_labels_remain_lazy", () =>
{
    var item = Create(); var shell = new ShellViewModel([item]);
    var calls = ListedItem.Formatter.Calls;
    shell.UpdateDateDisplay([item]);
    Assert(ListedItem.Formatter.Calls == calls, "Unused date properties formatted during tick.");
});
Run("unchanged_labels_do_not_emit_notifications", () =>
{
    var item = Create(); _ = item.ItemDateModified;
    var shell = new ShellViewModel([item]); var notifications = item.Notifications;
    shell.UpdateDateDisplay([item]);
    Assert(item.Notifications == notifications, "Stable date text notified bindings.");
});
Run("recycled_row_label_updates_when_realized_again", () =>
{
    var item = Create(); var original = item.ItemDateModified;
    ListedItem.Formatter.Prefix = "later:";
    var page = new BaseLayoutPage();
    page.RefreshRealizationDate(item, new StubArgs { Phase = 1 });
    Assert(item.ItemDateModified == original, "Later binding phase refreshed date text unnecessarily.");
    page.RefreshRealizationDate(item, new StubArgs());
    Assert(item.ItemDateModified != original, "Realization could show a cached old label.");
    ListedItem.Formatter.Prefix = "";
});
Run("layout_tick_skips_recycled_and_rebound_containers", () =>
{
    var current = Create(); var recycled = Create(); var rebound = Create();
    var oldCurrent = current.ItemDateModified; var oldRecycled = recycled.ItemDateModified; var oldRebound = rebound.ItemDateModified;
    var page = new BaseLayoutPage { ParentShellPageInstance = new StubShellPage { ShellViewModel = new ShellViewModel([current, recycled, rebound]) } };
    var currentContainer = new SelectorItem(); var recycledContainer = new SelectorItem(); var reboundContainer = new SelectorItem();
    page.Track(currentContainer, current); page.Track(recycledContainer, recycled); page.Track(reboundContainer, rebound);
    page.Recycle(recycledContainer); reboundContainer.Content = current;
    ListedItem.Formatter.Prefix = "tick:";
    page.UpdateDateDisplay();
    Assert(current.ItemDateModified != oldCurrent && recycled.ItemDateModified == oldRecycled && rebound.ItemDateModified == oldRebound, "Layout timer refreshed a stale container or missed its current row.");
    ListedItem.Formatter.Prefix = "";
});
Run("disposed_layout_and_missing_navigation_do_not_refresh_rows", () =>
{
    foreach (var dispose in new[] { true, false })
    {
        var item = Create(); var original = item.ItemDateModified;
        var page = new BaseLayoutPage { ParentShellPageInstance = new StubShellPage { ShellViewModel = new ShellViewModel([item]) } };
        page.Track(new SelectorItem(), item);
        if (dispose) page.Dispose(); else page.ClearNavigation();
        ListedItem.Formatter.Prefix = "closed:";
        page.UpdateDateDisplay();
        Assert(item.ItemDateModified == original, "A disposed or unloaded layout refreshed a row.");
        ListedItem.Formatter.Prefix = "";
    }
});
Run("date_notification_can_recycle_another_row_during_tick", () =>
{
    var first = Create(); var second = Create(); var original = second.ItemDateModified; _ = first.ItemDateModified;
    var page = new BaseLayoutPage { ParentShellPageInstance = new StubShellPage { ShellViewModel = new ShellViewModel([first, second]) } };
    var secondContainer = new SelectorItem();
    page.Track(new SelectorItem(), first); page.Track(secondContainer, second);
    first.NotificationCallback = _ => page.Recycle(secondContainer);
    ListedItem.Formatter.Prefix = "reentrant:";
    page.UpdateDateDisplay();
    Assert(second.ItemDateModified == original, "Date notification refreshed a row after it was recycled.");
    ListedItem.Formatter.Prefix = "";
});

var samples = new List<object>();
foreach (var cachedRows in new[] { 100, 100000 })
{
    var items = Enumerable.Range(0, 100000).Select(_ => Create()).ToArray();
    foreach (var item in items.Take(cachedRows)) _ = item.ItemDateModified;
    var shell = new ShellViewModel(items);
    var visible = items.Take(100).ToArray();
    var page = new BaseLayoutPage { ParentShellPageInstance = new StubShellPage { ShellViewModel = shell } };
    foreach (var item in visible) page.Track(new SelectorItem(), item);
    shell.BaselineUpdateDateDisplay(); shell.UpdateDateDisplay(visible); page.UpdateDateDisplay();
    var iteration = 0;
    for (; iteration < 20; iteration++)
    {
        if (iteration % 2 == 0) { Measure("PhaseIII", shell.BaselineUpdateDateDisplay); Measure("PhaseIVVisible100", () => shell.UpdateDateDisplay(visible)); Measure("PhaseIVRealized100", page.UpdateDateDisplay); }
        else { Measure("PhaseIVRealized100", page.UpdateDateDisplay); Measure("PhaseIVVisible100", () => shell.UpdateDateDisplay(visible)); Measure("PhaseIII", shell.BaselineUpdateDateDisplay); }
    }
    void Measure(string variant, Action action)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var calls = ListedItem.Formatter.Calls;
        var started = Stopwatch.GetTimestamp(); action(); var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        samples.Add(new { Variant=variant, CachedRows=cachedRows, TotalRows=items.Length, Iteration=iteration, DurationMs=elapsed, AllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated, FormatterCalls=ListedItem.Formatter.Calls-calls });
    }
}
var taskWorkspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(taskWorkspace, "tests-extra/Phase4Dates/source-metadata.json")));
var outputPath = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(taskWorkspace, "reports/phase4-date-contracts-and-benchmark.json"));
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(new { Source=metadata.RootElement.Clone(), Contracts=results, Samples=samples, Scope="Managed method benchmark. Actual date methods, ConcurrentCollection and application formatter; resources/app services substituted. Excludes WinUI/rendering and physical frames." }, new JsonSerializerOptions { WriteIndented=true }));
Console.WriteLine($"Passed {results.Count}/8 date contracts; {samples.Count} managed samples. {outputPath}");

void Run(string name, Action test) { test(); results.Add(new { Name=name, Passed=true }); }
static ListedItem Create() => new() { ItemDateModifiedReal=DateTimeOffset.Now.AddDays(-30), ItemDateCreatedReal=DateTimeOffset.Now.AddDays(-40), ItemDateAccessedReal=DateTimeOffset.Now.AddDays(-20) };
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
