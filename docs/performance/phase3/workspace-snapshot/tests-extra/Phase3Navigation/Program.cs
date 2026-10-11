using Files.App.ViewModels;
using System.Text.Json;

var results = new List<object>();
await Run("universal_cancel_during_eof_fetch_returns_cancellation", async () =>
{
    using var cts = new CancellationTokenSource();
    using var folder = new DelegateFolder((start, count) => { cts.Cancel(); return Task.FromResult<IReadOnlyList<IStorageItem>>([]); });
    var canceled = false;
    try { await UniversalStorageEnumerator.ListEntries(folder, new(folder, "synthetic"), cts.Token, -1, 32, _ => Task.CompletedTask); }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { canceled = true; }
    Assert(canceled, "EOF returned a successful canceled enumeration.");
});
await Run("universal_access_fallback_observes_cancellation_before_next_item", async () =>
{
    using var cts = new CancellationTokenSource();
    var fetches = 0;
    using var folder = new DelegateFolder((start, count) =>
    {
        if (++fetches == 1) return Task.FromException<IReadOnlyList<IStorageItem>>(new UnauthorizedAccessException());
        cts.Cancel(); return Task.FromResult<IReadOnlyList<IStorageItem>>([new BaseStorageFile()]);
    });
    var canceled = false;
    try { await UniversalStorageEnumerator.ListEntries(folder, new(folder, "synthetic"), cts.Token, -1, 32, _ => Task.CompletedTask); }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { canceled = true; }
    Assert(canceled && fetches == 2, "Access fallback swallowed cancellation or fetched another item.");
});
await Run("universal_cancelled_metadata_does_not_start_io", async () =>
{
    using var cts = new CancellationTokenSource(); cts.Cancel();
    var file = new CountedFile(); var canceled = false;
    try { await UniversalStorageEnumerator.AddFileAsync(file, null, cts.Token); }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { canceled = true; }
    Assert(canceled && file.Reads == 0, "Canceled metadata started a filesystem read.");
});
await Run("large_folder_cancel_still_requests_deferred_memory_trim", () =>
{
    var shell = new ShellViewModel();
    shell.Seed(Enumerable.Range(0, 10000).Select(x => $"file-{x}").ToArray());
    var before = AppMemoryHelper.Requests;
    shell.CancelLoadAndClearFiles();
    Assert(AppMemoryHelper.Requests == before + 1 && shell.LogicalNames.Length == 0, "Large folder memory trim was lost before clear.");
    return Task.CompletedTask;
});
await Run("native_enumeration_timeout_disposes_late_owned_result", async () =>
{
    var pending = new TaskCompletionSource<OwnedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
    var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var result = await StorageEnumerationTask.AwaitAsync(pending.Task, TimeSpan.FromMilliseconds(10), CancellationToken.None, resource => { resource.Dispose(); disposed.SetResult(); });
    Assert(result is null, "Timed out enumeration returned a live resource.");
    var late = new OwnedResource(); pending.SetResult(late);
    await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(late.Disposals == 1, "Abandoned native handle was not disposed once.");
});
await Run("native_enumeration_cancellation_unblocks_before_late_native_return", async () =>
{
    var pending = new TaskCompletionSource<OwnedResource>(TaskCreationOptions.RunContinuationsAsynchronously);
    var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var cts = new CancellationTokenSource();
    var wait = StorageEnumerationTask.AwaitAsync(pending.Task, TimeSpan.FromSeconds(5), cts.Token, resource => { resource.Dispose(); disposed.SetResult(); });
    cts.Cancel();
    var canceled = false;
    try { await wait.WaitAsync(TimeSpan.FromSeconds(1)); } catch (OperationCanceledException) { canceled = true; }
    Assert(canceled && !pending.Task.IsCompleted, "Cancel waited for the native query to finish.");
    var late = new OwnedResource(); pending.SetResult(late);
    await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(late.Disposals == 1, "Cancelled late native resource leaked.");
});
await Run("successful_native_enumeration_preserves_caller_ownership", async () =>
{
    var expected = new OwnedResource();
    var result = await StorageEnumerationTask.AwaitAsync(Task.FromResult(expected), TimeSpan.FromSeconds(5), CancellationToken.None, resource => resource.Dispose());
    Assert(ReferenceEquals(result, expected) && expected.Disposals == 0, "Successful enumeration disposed caller resource.");
    result!.Dispose();
});
await Run("cancelled_delayed_ui_publication_does_not_restore_old_location", async () =>
{
    var shell = new ShellViewModel();
    shell.Seed("A");
    shell.dispatcherQueue.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var pending = shell.ApplyFilesAndFoldersChangesAsync();
    await shell.dispatcherQueue.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    shell.CancelLoadAndClearFiles();
    shell.Seed("B");
    shell.dispatcherQueue.Gate.SetResult();
    await pending;
    Assert(shell.FilesAndFolders.Select(x => x.Name).SequenceEqual(["B"]), "Late UI publication restored A over B.");
});
await Run("newest_navigation_wins_over_running_and_queued_locations", async () =>
{
    var shell = new ShellViewModel();
    var oldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    shell.EnumerationOverride = (path, token) => path == "A" ? oldGate.Task : Task.CompletedTask;
    var completed = new List<string?>();
    shell.ItemLoadStatusChanged += (_, e) => { if (e.Status == ItemLoadStatusChangedEventArgs.ItemLoadStatus.Complete) completed.Add(e.Path); };
    var callbacks = new List<string>();
    var first = shell.LoadAsync("A", () => callbacks.Add("A"));
    var second = shell.LoadAsync("B", () => callbacks.Add("B"));
    var third = shell.LoadAsync("C", () => callbacks.Add("C"));
    oldGate.SetResult();
    await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));
    Assert(shell.EnumerationCalls.SequenceEqual(["A", "C"]), "Superseded queued navigation B enumerated.");
    Assert(shell.LogicalNames.SequenceEqual(["C"]), "Stale logical rows survived.");
    Assert(shell.FilesAndFolders.Select(x => x.Name).SequenceEqual(["C"]), "Stale visible rows survived.");
    Assert(completed.SequenceEqual(["C"]) && callbacks.SequenceEqual(["C"]), "Stale complete event or callback fired.");
});
await Run("cancelled_background_sort_cannot_replace_new_location", async () =>
{
    var shell = new ShellViewModel();
    shell.Seed("old-2", "old-1");
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var firstCompare = 0;
    NaturalStringComparer.BeforeCompare = () =>
    {
        if (Interlocked.Exchange(ref firstCompare, 1) == 0)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Sort gate timed out.");
        }
    };
    try
    {
        var pending = shell.SortAsync();
        Assert(entered.Wait(TimeSpan.FromSeconds(5)), "Background sort did not start.");
        shell.CancelLoadAndClearFiles();
        shell.Seed("new");
        release.Set();
        await pending;
        Assert(shell.LogicalNames.SequenceEqual(["new"]), "Cancelled sort replaced the new location.");
    }
    finally { release.Set(); NaturalStringComparer.BeforeCompare = null; }
});
await Run("filter_change_discards_delayed_snapshot", async () =>
{
    var shell = new ShellViewModel();
    shell.Seed("match", "other");
    shell.FilesAndFoldersFilter = "match";
    shell.dispatcherQueue.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var pending = shell.ApplyFilesAndFoldersChangesAsync();
    await shell.dispatcherQueue.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    shell.FilesAndFoldersFilter = "other";
    shell.dispatcherQueue.Gate.SetResult();
    await pending;
    await shell.ApplyFilesAndFoldersChangesAsync();
    Assert(shell.FilesAndFolders.Select(x => x.Name).SequenceEqual(["other"]), "Old filter snapshot survived.");
});
await Run("repeated_cancel_navigation_releases_semaphores", async () =>
{
    var shell = new ShellViewModel();
    for (var round = 0; round < 50; round++)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.EnumerationOverride = (path, token) => path.StartsWith("old") ? gate.Task : Task.CompletedTask;
        var old = shell.LoadAsync($"old-{round}");
        var current = shell.LoadAsync($"current-{round}");
        gate.SetResult();
        await Task.WhenAll(old, current).WaitAsync(TimeSpan.FromSeconds(5));
        Assert(shell.LogicalNames.SequenceEqual([$"current-{round}"]), "Navigation drift after cancellation.");
    }
});
await Run("search_names_publish_without_waiting_for_icons", async () =>
{
    var shell = new ShellViewModel();
    var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
    Ioc.IconOverride = () => gate.Task;
    var search = new FolderSearch
    {
        HasIncompleteResults = true,
        SearchOverride = (results, token) => { results.Add(new ListedItem { ItemNameRaw = "old-search" }); return Task.CompletedTask; }
    };
    try
    {
        var pending = shell.SearchAsync(search);
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert(shell.FilesAndFolders.Select(x => x.Name).SequenceEqual(["old-search"]), "Names waited for icon extraction.");
        await shell.LoadAsync("current-navigation").WaitAsync(TimeSpan.FromSeconds(5));
        gate.SetResult(null);
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(shell.LogicalNames.SequenceEqual(["current-navigation"]), "Cancelled search final preload replaced navigation.");
        Assert(shell.FilesAndFolders.Select(x => x.Name).SequenceEqual(["current-navigation"]), "Cancelled search final publication replaced navigation.");
        Assert(!search.HasTickSubscribers && shell.SearchSource is null, "Completed search retained event subscriber or source.");
    }
    finally { gate.TrySetResult(null); Ioc.IconOverride = null; }
});
await Run("incomplete_current_search_is_marked_and_releases_source", async () =>
{
    var shell = new ShellViewModel();
    var search = new FolderSearch
    {
        HasIncompleteResults = true,
        SearchOverride = (results, token) => { results.Add(new ListedItem { ItemNameRaw = "result" }); return Task.CompletedTask; }
    };
    await shell.SearchAsync(search);
    Assert(shell.SearchHeaderTitle.Contains(Strings.SearchResultsIncomplete, StringComparison.Ordinal), "Incomplete search was presented as complete.");
    Assert(!shell.IsLoadingItems && shell.SearchSource is null && !search.HasTickSubscribers, "Search source or status leaked.");
});
await Run("repeated_search_sources_are_not_retained_by_folder_token", async () =>
{
    var shell = new ShellViewModel();
    var sources = new List<WeakReference>();
    for (var repeat = 0; repeat < 100; repeat++)
    {
        var search = new FolderSearch { SearchOverride = (results, token) => { sources.Add(new WeakReference(shell.SearchSource!)); return Task.CompletedTask; } };
        await shell.SearchAsync(search);
    }
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Assert(sources.Count(x => x.IsAlive) < 3, "Folder cancellation source retains finished search sources.");
});
await Run("thumbnail_retry_keeps_generation_link_until_background_work_finishes", async () =>
{
    var shell = new ShellViewModel();
    using var parent = new CancellationTokenSource();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var backgroundToken = CancellationToken.None;
    shell.ThumbnailOverride = async (token, awaitBackground) =>
    {
        Assert(awaitBackground, "Retry did not await its scheduled background thumbnail work.");
        backgroundToken = token;
        entered.SetResult();
        await gate.Task.WaitAsync(token);
    };
    var pending = shell.ScheduleRetry(parent.Token);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    parent.Cancel();
    await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(backgroundToken.IsCancellationRequested, "Retry disposed its generation link before background cancellation.");
    Assert(shell.PendingRetryCount == 0, "Finished retry remained in debounce map.");
    var disposed = false;
    try { _ = pending.Source.Token; } catch (ObjectDisposedException) { disposed = true; }
    Assert(disposed, "Owned retry source leaked.");
});
await Run("cancelled_thumbnail_delay_removes_debounce_entry", async () =>
{
    var shell = new ShellViewModel();
    using var parent = new CancellationTokenSource();
    var calls = 0;
    shell.ThumbnailOverride = (token, awaitBackground) => { calls++; return Task.CompletedTask; };
    var pending = shell.ScheduleRetry(parent.Token, delayMs: 1000);
    parent.Cancel();
    await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(calls == 0 && shell.PendingRetryCount == 0, "Canceled delay retained retry or loaded thumbnail.");
});
await Run("viewport_cancel_survives_metadata_return_and_requeue", () =>
{
    var shell = new ShellViewModel();
    var item = new ListedItem { ItemNameRaw = "visible" };
    using var oldSource = new CancellationTokenSource();
    var token = shell.TestPropertyToken(item, oldSource.Token);
    using var detached = CancellationTokenSource.CreateLinkedTokenSource(token);
    oldSource.Cancel();
    Assert(detached.IsCancellationRequested, "Completed row lost its cancellation link.");
    using var newSource = new CancellationTokenSource();
    var current = shell.TestPropertyToken(item, newSource.Token);
    Assert(!current.IsCancellationRequested && shell.PropertyLifetimeCount == 1, "New viewport retained cancelled lifetime.");
    shell.CancelExtendedPropertiesLoadingForItem(item);
    Assert(current.IsCancellationRequested && shell.PropertyLifetimeCount == 0, "Recycled row retained live work.");
    return Task.CompletedTask;
});
await Run("viewport_navigation_cancels_and_releases_all_links", () =>
{
    var shell = new ShellViewModel();
    using var parent = new CancellationTokenSource();
    var tokens = Enumerable.Range(0, 1000).Select(i => shell.TestPropertyToken(new ListedItem { ItemNameRaw = i.ToString() }, parent.Token)).ToArray();
    shell.CancelExtendedPropertiesLoading();
    Assert(shell.PropertyLifetimeCount == 0 && tokens.All(token => token.IsCancellationRequested), "Navigation left viewport work alive.");
    return Task.CompletedTask;
});
await Run("old_metadata_completion_cannot_clear_new_layout_generation", () =>
{
    var shell = new ShellViewModel();
    var item = new ListedItem { ItemNameRaw = "shared-row", ItemPropertiesInitialized = true };
    using var oldSource = new CancellationTokenSource();
    using var currentSource = new CancellationTokenSource();
    shell.TestBeginPropertyLoad(item, oldSource.Token);
    oldSource.Cancel();
    shell.TestBeginPropertyLoad(item, currentSource.Token);
    Assert(!shell.TestCompletePropertyLoad(item, oldSource.Token, true), "Old layout owned the new row load.");
    Assert(item.ItemPropertiesInitialized && shell.PendingPropertyCount == 1, "Old cancellation reset current initialization or removed its load.");
    Assert(shell.TestCompletePropertyLoad(item, currentSource.Token, false) && shell.PendingPropertyCount == 0, "Current generation could not release its load.");
    return Task.CompletedTask;
});
await Run("canceled_search_cannot_clear_new_navigation_loading_state", async () =>
{
    var shell = new ShellViewModel();
    var searchGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var navigationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var navigationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var search = new FolderSearch { SearchOverride = async (results, token) => { await searchGate.Task; token.ThrowIfCancellationRequested(); } };
    var pendingSearch = shell.SearchAsync(search);
    shell.EnumerationOverride = async (path, token) => { navigationEntered.SetResult(); await navigationGate.Task; };
    var pendingNavigation = shell.LoadAsync("new-navigation");
    await navigationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    searchGate.SetResult();
    await pendingSearch.WaitAsync(TimeSpan.FromSeconds(2));
    Assert(shell.IsLoadingItems && !shell.IsSearchResults, "Canceled search cleared the new navigation spinner.");
    navigationGate.SetResult();
    await pendingNavigation.WaitAsync(TimeSpan.FromSeconds(2));
    Assert(!shell.IsLoadingItems && shell.LogicalNames.SequenceEqual(["new-navigation"]), "Current navigation did not complete.");
});
await Run("small_navigation_does_not_request_full_collection", () =>
{
    var shell = new ShellViewModel();
    shell.Seed(Enumerable.Range(0, 1000).Select(i => i.ToString()).ToArray());
    var before = AppMemoryHelper.Requests;
    shell.CancelLoadAndClearFiles();
    Assert(AppMemoryHelper.Requests == before, "Small navigation induced a full collection.");
    return Task.CompletedTask;
});
File.WriteAllText(args.Length > 0 ? args[0] : "reports/phase3-navigation-shell-contracts.json", JsonSerializer.Serialize(new { sourceMethods = "ShellViewModel captured verbatim by Prepare-ShellContracts.ps1", dependencies = "Controlled dispatcher/storage and no WinUI runtime", results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{results.Count} ShellViewModel behavioral contracts passed.");

async Task Run(string name, Func<Task> action)
{
    await action();
    results.Add(new { name, passed = true });
}
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
sealed class OwnedResource : IDisposable { public int Disposals; public void Dispose() => Interlocked.Increment(ref Disposals); }
sealed class DelegateFolder(Func<uint, uint, Task<IReadOnlyList<IStorageItem>>> fetch) : BaseStorageFolder
{
    public override Task<IReadOnlyList<IStorageItem>> GetItemsAsync(uint start, uint count) => fetch(start, count);
}
sealed class CountedFile : BaseStorageFile
{
    public int Reads;
    public override Task<BasicProperties> GetBasicPropertiesAsync() { Reads++; return base.GetBasicPropertiesAsync(); }
}
