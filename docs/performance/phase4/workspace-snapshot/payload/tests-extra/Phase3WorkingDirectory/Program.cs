using Files.App.ViewModels;
using Files.App.Views.Shells;
using System.Text.Json;
using System.Collections.Specialized;

var results = new List<object>();
var failures = 0;

await Run("pending_root_flag_becomes_false_when_latest_A_invalidates_B", async () =>
{
    var shell = new ShellViewModel();
    var root = Pending<StorageFolderWithPath?>();
    DriveHelpers.RootHandler = _ => root.Task;
    var older = shell.SetWorkingDirectoryAsync(@"D:\B");
    Assert(shell.IsWorkingDirectoryUpdatePending && shell.WorkingDirectory == @"C:\A", "Pending root was not visible while committed path stayed A.");
    await shell.SetWorkingDirectoryAsync(@"C:\A");
    Assert(!shell.IsWorkingDirectoryUpdatePending, "Latest A did not clear pending root intent.");
    root.SetResult(new(@"D:\"));
    await older;
    Assert(!shell.IsWorkingDirectoryUpdatePending && shell.RootPath == @"C:\", "Late B revived pending/root state.");
});

await Run("blank_latest_invalidates_B_and_clears_pending_flag", async () =>
{
    var shell = new ShellViewModel();
    var root = Pending<StorageFolderWithPath?>();
    DriveHelpers.RootHandler = _ => root.Task;
    var older = shell.SetWorkingDirectoryAsync(@"D:\B");
    Assert(shell.IsWorkingDirectoryUpdatePending, "Root was not pending.");
    await shell.SetWorkingDirectoryAsync(" ");
    Assert(!shell.IsWorkingDirectoryUpdatePending, "Blank latest kept obsolete pending intent.");
    root.SetResult(new(@"D:\"));
    await older;
    Assert(shell.WorkingDirectory == @"C:\A" && !shell.IsWorkingDirectoryUpdatePending, "Blank latest allowed B to commit.");
});

await Run("Git_only_await_after_path_commit_is_not_pending_root", async () =>
{
    var shell = new ShellViewModel();
    var head = Pending<string?>();
    var entered = Pending<bool>();
    GitHelpers.HeadHandler = _ => { entered.TrySetResult(true); return head.Task; };
    var pending = shell.SetWorkingDirectoryAsync(@"C:\B");
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(shell.WorkingDirectory == @"C:\B" && !shell.IsWorkingDirectoryUpdatePending, "Git-only waiting was treated as an uncommitted root.");
    head.SetResult("main");
    await pending;
});

foreach (var latest in new[] { @"E:\C", @"C:\A" })
{
    await Run("delayed_cross_drive_root_cannot_overwrite_latest_" + (latest[0] == 'C' ? "A" : "C"), async () =>
    {
        var shell = new ShellViewModel();
        var root = Pending<StorageFolderWithPath?>();
        DriveHelpers.RootHandler = path => path == @"D:\B" ? root.Task : Task.FromResult<StorageFolderWithPath?>(new(Path.GetPathRoot(path)!));
        var older = shell.SetWorkingDirectoryAsync(@"D:\B");
        await shell.SetWorkingDirectoryAsync(latest).WaitAsync(TimeSpan.FromSeconds(5));
        root.SetResult(new(@"D:\"));
        await older.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(shell.WorkingDirectory == latest && shell.RootPath == Path.GetPathRoot(latest), "Late B root replaced the latest committed location/root.");
        Assert(shell.GitDirectory == "repo:" + latest && shell.ThumbnailUpdates == 1, "Late B continued Git or UI work.");
    });
}

foreach (var latest in new[] { "", "   ", "Home" })
{
    await Run("latest_" + (latest == "Home" ? "home" : latest.Length == 0 ? "empty" : "whitespace") + "_invalidates_delayed_root", async () =>
    {
        var shell = new ShellViewModel();
        var root = Pending<StorageFolderWithPath?>();
        DriveHelpers.RootHandler = _ => root.Task;
        var older = shell.SetWorkingDirectoryAsync(@"D:\B");
        await shell.SetWorkingDirectoryAsync(latest).WaitAsync(TimeSpan.FromSeconds(5));
        root.SetResult(new(@"D:\"));
        await older.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(shell.WorkingDirectory == (latest == "Home" ? "Home" : @"C:\A"), "Empty/Home latest update allowed delayed B to commit.");
        Assert(shell.RootPath == (latest == "Home" ? null : @"C:\"), "Old root was installed after invalidation.");
    });
}

await Run("reentrant_modified_event_preserves_newer_location", async () =>
{
    var shell = new ShellViewModel();
    Task? newer = null;
    shell.WorkingDirectoryModified += (_, e) => { if (e.Path == @"D:\B") newer = shell.SetWorkingDirectoryAsync(@"C:\C"); };
    await shell.SetWorkingDirectoryAsync(@"D:\B");
    await newer!.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(shell.WorkingDirectory == @"C:\C" && shell.RootPath == @"C:\" && shell.ThumbnailUpdates == 1, "Outer event continuation superseded its reentrant newer update.");
});

await Run("reentrant_modified_event_disposal_stops_root_lookup", async () =>
{
    var shell = new ShellViewModel();
    var rootCalls = 0;
    DriveHelpers.RootHandler = path => { rootCalls++; return Task.FromResult<StorageFolderWithPath?>(new(path)); };
    shell.WorkingDirectoryModified += (_, _) => shell.DisposeForTest();
    await shell.SetWorkingDirectoryAsync(@"D:\B");
    Assert(rootCalls == 0 && shell.WorkingDirectory == @"C:\A" && shell.ThumbnailUpdates == 0, "Disposed event continuation invoked storage or committed state.");
});

await Run("disposal_during_root_await_prevents_commit", async () =>
{
    var shell = new ShellViewModel();
    var root = Pending<StorageFolderWithPath?>();
    DriveHelpers.RootHandler = _ => root.Task;
    var pending = shell.SetWorkingDirectoryAsync(@"D:\B");
    shell.DisposeForTest();
    root.SetResult(new(@"D:\"));
    await pending.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(shell.WorkingDirectory == @"C:\A" && shell.RootPath == @"C:\" && shell.ThumbnailUpdates == 0, "Disposed root completion committed state.");
});

await Run("delayed_git_lookup_same_path_ABA_cannot_overwrite_new_repository", async () =>
{
    var shell = new ShellViewModel();
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var firstA = 0;
    GitHelpers.RepositoryHandler = (path, _) =>
    {
        if (path == @"C:\A" && Interlocked.Increment(ref firstA) == 1)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Git gate timeout.");
            return "old-A";
        }
        return "new:" + path;
    };
    var older = shell.SetWorkingDirectoryAsync(@"C:\A");
    try
    {
        Assert(entered.Wait(TimeSpan.FromSeconds(5)), "Old Git lookup did not enter.");
        await shell.SetWorkingDirectoryAsync(@"C:\B");
        await shell.SetWorkingDirectoryAsync(@"C:\A");
    }
    finally { release.Set(); }
    await older.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(shell.WorkingDirectory == @"C:\A" && shell.GitDirectory == @"new:C:\A" && shell.ThumbnailUpdates == 2, "ABA location equality accepted the obsolete Git result.");
});

await Run("delayed_head_same_path_ABA_cannot_overwrite_new_validity", async () =>
{
    var shell = new ShellViewModel();
    var head = Pending<string?>();
    var entered = Pending<bool>();
    var calls = 0;
    GitHelpers.RepositoryHandler = (path, _) => "repo-" + Interlocked.Increment(ref calls) + ":" + path;
    GitHelpers.HeadHandler = repo => { if (repo!.StartsWith("repo-1:")) { entered.TrySetResult(true); return head.Task; } return Task.FromResult<string?>(null); };
    var older = shell.SetWorkingDirectoryAsync(@"C:\A");
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await shell.SetWorkingDirectoryAsync(@"C:\B");
    await shell.SetWorkingDirectoryAsync(@"C:\A");
    head.SetResult("obsolete-main");
    await older.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(shell.WorkingDirectory == @"C:\A" && shell.GitDirectory == @"repo-3:C:\A" && !shell.IsValidGitDirectory && shell.ThumbnailUpdates == 2, "Old head lookup replaced latest ABA validity or UI notifications.");
});

await Run("disposal_during_head_await_prevents_UI_notifications", async () =>
{
    var shell = new ShellViewModel();
    var head = Pending<string?>();
    var entered = Pending<bool>();
    GitHelpers.HeadHandler = _ => { entered.TrySetResult(true); return head.Task; };
    var pending = shell.SetWorkingDirectoryAsync(@"C:\B");
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    shell.DisposeForTest();
    head.SetResult("main");
    await pending.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!shell.IsValidGitDirectory && shell.ThumbnailUpdates == 0 && shell.PropertyNotifications.Count == 0, "Disposed head completion emitted UI changes.");
});

await Run("normal_cross_drive_update_preserves_root_git_and_notifications", async () =>
{
    var shell = new ShellViewModel();
    await shell.SetWorkingDirectoryAsync(@"D:\B");
    Assert(shell.WorkingDirectory == @"D:\B" && shell.RootPath == @"D:\" && shell.GitDirectory == @"repo:D:\B" && shell.IsValidGitDirectory, "Ordinary working-directory behavior regressed.");
    Assert(shell.FilesAndFoldersFilter is null && shell.ThumbnailUpdates == 1 && shell.PropertyNotifications.SequenceEqual(["WorkingDirectory", "ShowFilterHeader"]), "Ordinary filter/thumbnail/property behavior regressed.");
});

await Run("library_update_clears_storage_root_and_retains_event_metadata", async () =>
{
    var shell = new ShellViewModel();
    WorkingDirectoryModifiedEventArgs? observed = null;
    shell.WorkingDirectoryModified += (_, e) => observed = e;
    await shell.SetWorkingDirectoryAsync("library:synthetic");
    Assert(shell.RootPath is null && shell.CurrentFolderPath is null && observed is { IsLibrary: true, Name: "Library" } && shell.WorkingDirectory == "library:synthetic", "Library storage/event behavior regressed.");
});

await Run("disposed_entry_does_not_invoke_modified_event_or_git", async () =>
{
    var shell = new ShellViewModel();
    var modified = false;
    shell.WorkingDirectoryModified += (_, _) => modified = true;
    shell.DisposeForTest();
    await shell.SetWorkingDirectoryAsync(@"D:\B");
    Assert(!modified && shell.WorkingDirectory == @"C:\A" && shell.GitDirectory == "initial-repo", "Disposed entry changed state.");
});

await Run("delayed_breadcrumb_B_cannot_replace_latest_C", async () =>
{
    var page = PageWithInitialBreadcrumb();
    var olderComponents = Pending<IReadOnlyList<PathBoxItem>>();
    StorageFileExtensions.ComponentsHandler = path => path == "B" ? olderComponents.Task : Components(path);
    var older = page.UpdatePathUIToWorkingDirectoryAsync("B");
    await page.UpdatePathUIToWorkingDirectoryAsync("C");
    olderComponents.SetResult([new() { Path = "B", Title = "old-B" }]);
    await older.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(page.ToolbarViewModel.PathComponents.Select(x => x.Path).SequenceEqual(["C"]), "Delayed B replaced C breadcrumbs.");
});

await Run("delayed_breadcrumb_same_path_ABA_is_rejected", async () =>
{
    var page = PageWithInitialBreadcrumb();
    var olderComponents = Pending<IReadOnlyList<PathBoxItem>>();
    var calls = 0;
    StorageFileExtensions.ComponentsHandler = path => path == "A" && ++calls == 1 ? olderComponents.Task : Components("latest-" + path);
    var older = page.UpdatePathUIToWorkingDirectoryAsync("A");
    await page.UpdatePathUIToWorkingDirectoryAsync("B");
    await page.UpdatePathUIToWorkingDirectoryAsync("A");
    olderComponents.SetResult([new() { Path = "A", Title = "old-A" }]);
    await older.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(page.ToolbarViewModel.PathComponents[0].Path == "latest-A", "Path equality accepted obsolete ABA breadcrumbs.");
});

await Run("single_override_invalidates_delayed_regular_breadcrumbs", async () =>
{
    var page = PageWithInitialBreadcrumb();
    var olderComponents = Pending<IReadOnlyList<PathBoxItem>>();
    StorageFileExtensions.ComponentsHandler = _ => olderComponents.Task;
    var older = page.UpdatePathUIToWorkingDirectoryAsync("B");
    await page.UpdatePathUIToWorkingDirectoryAsync(null, "Synthetic search");
    olderComponents.SetResult([new() { Path = "B" }]);
    await older.WaitAsync(TimeSpan.FromSeconds(5));
    var item = page.ToolbarViewModel.PathComponents.Single();
    Assert(page.ToolbarViewModel.IsSingleItemOverride && item.Path is null && item.Title == "Synthetic search" && item.ChevronToolTip == "Open Synthetic search", "Old path computation replaced the single override.");
});

await Run("null_latest_breadcrumb_request_invalidates_older_before_throw", async () =>
{
    var page = PageWithInitialBreadcrumb();
    var olderComponents = Pending<IReadOnlyList<PathBoxItem>>();
    StorageFileExtensions.ComponentsHandler = _ => olderComponents.Task;
    var older = page.UpdatePathUIToWorkingDirectoryAsync("B");
    var rejected = false;
    try { await page.UpdatePathUIToWorkingDirectoryAsync(null); } catch (InvalidOperationException) { rejected = true; }
    olderComponents.SetResult([new() { Path = "B" }]);
    await older.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(rejected && page.ToolbarViewModel.PathComponents[0].Path == "A", "Null latest request failed to invalidate the older breadcrumb continuation.");
});

await Run("breadcrumb_disposal_after_await_preserves_existing_components", async () =>
{
    var page = PageWithInitialBreadcrumb();
    var components = Pending<IReadOnlyList<PathBoxItem>>();
    StorageFileExtensions.ComponentsHandler = _ => components.Task;
    var pending = page.UpdatePathUIToWorkingDirectoryAsync("B");
    page.DisposeForTest();
    components.SetResult([new() { Path = "B" }]);
    await pending.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(page.ToolbarViewModel.PathComponents[0].Path == "A", "Disposed breadcrumb completion changed its collection.");
});

await Run("breadcrumb_disposed_entry_skips_provider_and_override", async () =>
{
    var page = PageWithInitialBreadcrumb();
    var calls = 0;
    StorageFileExtensions.ComponentsHandler = path => { calls++; return Components(path); };
    page.DisposeForTest();
    await page.UpdatePathUIToWorkingDirectoryAsync("B");
    await page.UpdatePathUIToWorkingDirectoryAsync(null, "Ignored");
    Assert(calls == 0 && page.ToolbarViewModel.PathComponents[0].Path == "A" && !page.ToolbarViewModel.IsSingleItemOverride, "Disposed breadcrumb entry did work.");
});

await Run("filter_property_reentrancy_suppresses_old_modified_event", async () =>
{
    var shell = new ShellViewModel();
    var reentered = false;
    Task? latest = null;
    var events = new List<string?>();
    shell.WorkingDirectoryModified += (_, e) => events.Add(e.Path);
    shell.FilterChanged = () => { if (!reentered) { reentered = true; latest = shell.SetWorkingDirectoryAsync(@"C:\C"); } };
    await shell.SetWorkingDirectoryAsync(@"D:\B");
    await latest!.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(events.SequenceEqual([@"C:\C"]) && shell.WorkingDirectory == @"C:\C", "Old SetWorkingDirectory emitted its event after reentrant filter navigation.");
});

await Run("thumbnail_delayed_A_cannot_replace_new_B_icon", async () =>
{
    var shell = new ShellViewModel();
    var olderIcon = Pending<string?>();
    NavigationHelpers.IconHandler = path => path == @"C:\A" ? olderIcon.Task : Task.FromResult<string?>("latest:" + path);
    var older = shell.UpdateFolderThumbnailImageSource();
    await shell.SetWorkingDirectoryAsync(@"C:\B");
    olderIcon.SetResult("obsolete-A");
    await older;
    Assert(shell.FolderThumbnailImageSource == @"latest:C:\B", "Delayed A thumbnail replaced B.");
});

await Run("thumbnail_same_path_ABA_rejects_old_revision_icon", async () =>
{
    var shell = new ShellViewModel();
    var olderIcon = Pending<string?>();
    var firstA = 0;
    NavigationHelpers.IconHandler = path => path == @"C:\A" && Interlocked.Increment(ref firstA) == 1 ? olderIcon.Task : Task.FromResult<string?>("latest:" + path);
    var older = shell.UpdateFolderThumbnailImageSource();
    await shell.SetWorkingDirectoryAsync(@"C:\B");
    await shell.SetWorkingDirectoryAsync(@"C:\A");
    olderIcon.SetResult("obsolete-same-A");
    await older;
    Assert(shell.FolderThumbnailImageSource == @"latest:C:\A", "Path equality accepted an obsolete ABA thumbnail.");
});

await Run("thumbnail_disposal_after_icon_await_prevents_assignment", async () =>
{
    var shell = new ShellViewModel();
    var icon = Pending<string?>();
    NavigationHelpers.IconHandler = _ => icon.Task;
    var pending = shell.UpdateFolderThumbnailImageSource();
    shell.DisposeForTest();
    icon.SetResult("obsolete-disposed");
    await pending;
    Assert(shell.FolderThumbnailImageSource == "initial-icon", "Disposed thumbnail completion assigned an icon.");
});

await Run("thumbnail_disposed_entry_does_not_start_icon_work", async () =>
{
    var shell = new ShellViewModel();
    shell.DisposeForTest();
    await shell.UpdateFolderThumbnailImageSource();
    Assert(shell.ThumbnailUpdates == 0 && shell.FolderThumbnailImageSource == "initial-icon", "Disposed thumbnail entry started icon work.");
});

await Run("thumbnail_pending_cross_drive_root_skips_old_committed_A_icon", async () =>
{
    var shell = new ShellViewModel();
    var root = Pending<StorageFolderWithPath?>();
    DriveHelpers.RootHandler = _ => root.Task;
    var pending = shell.SetWorkingDirectoryAsync(@"D:\B");
    await shell.UpdateFolderThumbnailImageSource();
    Assert(shell.IsWorkingDirectoryUpdatePending && shell.ThumbnailUpdates == 0, "Thumbnail read old A while B's root was pending.");
    root.SetResult(new(@"D:\"));
    await pending;
    Assert(shell.FolderThumbnailImageSource == @"icon:D:\B", "Current B thumbnail failed after root completion.");
});

await Run("thumbnail_inflight_A_is_discarded_when_B_root_becomes_pending", async () =>
{
    var shell = new ShellViewModel();
    var icon = Pending<string?>();
    NavigationHelpers.IconHandler = path => path == @"C:\A" ? icon.Task : Task.FromResult<string?>("icon:" + path);
    var older = shell.UpdateFolderThumbnailImageSource();
    var root = Pending<StorageFolderWithPath?>();
    DriveHelpers.RootHandler = _ => root.Task;
    var pending = shell.SetWorkingDirectoryAsync(@"D:\B");
    icon.SetResult("obsolete-pending-A");
    await older;
    Assert(shell.FolderThumbnailImageSource == "initial-icon", "Old icon was assigned while a new root was pending.");
    root.SetResult(new(@"D:\"));
    await pending;
});

await Run("thumbnail_blank_latest_invalidates_inflight_same_path_icon", async () =>
{
    var shell = new ShellViewModel();
    var icon = Pending<string?>();
    NavigationHelpers.IconHandler = _ => icon.Task;
    var older = shell.UpdateFolderThumbnailImageSource();
    await shell.SetWorkingDirectoryAsync("");
    icon.SetResult("obsolete-empty-intent");
    await older;
    Assert(shell.FolderThumbnailImageSource == "initial-icon", "Blank latest intent accepted older thumbnail work.");
});

await Run("breadcrumb_Clear_reentrancy_prevents_old_components_append", async () =>
{
    var page = PageWithInitialBreadcrumb();
    Task? latest = null;
    var reentered = false;
    page.ToolbarViewModel.PathComponents.CollectionChanged += (_, e) => { if (!reentered && e.Action == NotifyCollectionChangedAction.Reset) { reentered = true; latest = page.UpdatePathUIToWorkingDirectoryAsync("C"); } };
    await page.UpdatePathUIToWorkingDirectoryAsync("B");
    await latest!;
    Assert(page.ToolbarViewModel.PathComponents.Select(x => x.Path).SequenceEqual(["C"]), "Old B appended components after Clear triggered C.");
});

await Run("breadcrumb_Add_reentrancy_prevents_remaining_old_components", async () =>
{
    var page = PageWithInitialBreadcrumb();
    StorageFileExtensions.ComponentsHandler = path => Task.FromResult<IReadOnlyList<PathBoxItem>>(path == "B" ? [new() { Path = "B-1" }, new() { Path = "B-2" }] : [new() { Path = path }]);
    Task? latest = null;
    var reentered = false;
    page.ToolbarViewModel.PathComponents.CollectionChanged += (_, e) => { if (!reentered && e.Action == NotifyCollectionChangedAction.Add) { reentered = true; latest = page.UpdatePathUIToWorkingDirectoryAsync("C"); } };
    await page.UpdatePathUIToWorkingDirectoryAsync("B");
    await latest!;
    Assert(page.ToolbarViewModel.PathComponents.Select(x => x.Path).SequenceEqual(["C"]), "Old B-2 appended after Add triggered C.");
});

await Run("breadcrumb_override_Clear_reentrancy_prevents_old_override", async () =>
{
    var page = PageWithInitialBreadcrumb();
    Task? latest = null;
    var reentered = false;
    page.ToolbarViewModel.PathComponents.CollectionChanged += (_, e) => { if (!reentered && e.Action == NotifyCollectionChangedAction.Reset) { reentered = true; latest = page.UpdatePathUIToWorkingDirectoryAsync("C"); } };
    await page.UpdatePathUIToWorkingDirectoryAsync(null, "Obsolete override");
    await latest!;
    Assert(page.ToolbarViewModel.PathComponents.Select(x => x.Path).SequenceEqual(["C"]) && !page.ToolbarViewModel.IsSingleItemOverride, "Clear callback C was overwritten by old single override.");
});

await Run("breadcrumb_override_property_reentrancy_prevents_old_override_append", async () =>
{
    var page = PageWithInitialBreadcrumb();
    Task? latest = null;
    page.ToolbarViewModel.OverrideChanged = () => latest = page.UpdatePathUIToWorkingDirectoryAsync("C");
    await page.UpdatePathUIToWorkingDirectoryAsync(null, "Obsolete override");
    await latest!;
    Assert(page.ToolbarViewModel.PathComponents.Select(x => x.Path).SequenceEqual(["C"]), "Override property callback C was overwritten by old override append.");
});

await Run("breadcrumb_Clear_disposal_prevents_old_components_append", async () =>
{
    var page = PageWithInitialBreadcrumb();
    page.ToolbarViewModel.PathComponents.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) page.DisposeForTest(); };
    await page.UpdatePathUIToWorkingDirectoryAsync("B");
    Assert(page.ToolbarViewModel.PathComponents.Count == 0, "Disposed Clear continuation appended B.");
});

await Run("literal_modern_route_latest_A_is_forced_while_cross_drive_B_root_pending", async () =>
{
    var page = new ModernShellPage();
    var shell = page.ShellViewModel!;
    var root = Pending<StorageFolderWithPath?>();
    DriveHelpers.RootHandler = _ => root.Task;
    var older = shell.SetWorkingDirectoryAsync(@"D:\B");
    page.ItemDisplayFrame.NavigateHandler = args => shell.SetWorkingDirectoryAsync(args.NavPathParam);
    page.NavigateToPath(@"C:\A", null);
    await page.ItemDisplayFrame.LastNavigateTask.WaitAsync(TimeSpan.FromSeconds(5));
    root.SetResult(new(@"D:\"));
    await older;
    Assert(page.ItemDisplayFrame.Navigations.Count == 1 && shell.WorkingDirectory == @"C:\A" && shell.RootPath == @"C:\" && !shell.IsWorkingDirectoryUpdatePending, "Same committed A was ignored while B had an uncommitted root.");
});

await Run("literal_modern_route_stable_same_location_still_skips_reload", () =>
{
    var page = new ModernShellPage();
    page.NavigateToPath(@"C:\A\", null);
    Assert(page.ItemDisplayFrame.Navigations.Count == 0 && !page.InstanceViewModel.FolderSettings.IsLayoutModeChanging, "Stable same-location route lost its no-op behavior.");
    return Task.CompletedTask;
});

await Run("literal_modern_route_explicit_associated_arguments_still_navigate", () =>
{
    var page = new ModernShellPage();
    var arguments = new NavigationArguments { NavPathParam = @"C:\A", AssociatedTabInstance = page };
    page.NavigateToPath(@"C:\A", typeof(DetailsPage), arguments);
    Assert(page.ItemDisplayFrame.Navigations.Count == 1 && ReferenceEquals(page.ItemDisplayFrame.Navigations[0], arguments), "Explicit associated navigation arguments were dropped.");
    return Task.CompletedTask;
});

await Run("literal_modern_route_tag_parameter_preserves_same_path_reload", () =>
{
    var page = new ModernShellPage { TabBarItemParameter = new() { NavigationParameter = "tag:synthetic" } };
    page.NavigateToPath(@"C:\A", null);
    Assert(page.ItemDisplayFrame.Navigations.Count == 1, "Tag same-path reload was dropped.");
    return Task.CompletedTask;
});

await Run("literal_modern_route_empty_location_never_navigates_while_pending", async () =>
{
    var page = new ModernShellPage();
    var root = Pending<StorageFolderWithPath?>();
    DriveHelpers.RootHandler = _ => root.Task;
    var older = page.ShellViewModel!.SetWorkingDirectoryAsync(@"D:\B");
    page.NavigateToPath("", null);
    Assert(page.ItemDisplayFrame.Navigations.Count == 0, "Empty pending route created an invalid navigation.");
    page.ShellViewModel.DisposeForTest();
    root.SetResult(new(@"D:\"));
    await older;
});

var output = args.Length > 0 ? args[0] : "reports/phase3-working-directory-contracts.json";
var sourceHashPath = Path.Combine(AppContext.BaseDirectory, "source-hashes.json");
var hashes = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(sourceHashPath));
var evidence = JsonSerializer.Serialize(new { capturedUtc = DateTime.UtcNow, sourceMethods = "SetWorkingDirectoryAsync, IsWorkingDirectoryUpdatePending, UpdateFolderThumbnailImageSource, UpdatePathUIToWorkingDirectoryAsync and ModernShellPage.NavigateToPath captured verbatim by Prepare-WorkingDirectoryContracts.ps1", sources = hashes, dependencies = "Deterministic root/Git/head/icon/breadcrumb/frame substitutes; no WinUI runtime or real filesystem operations", passed = failures == 0, total = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(output, evidence.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
Console.WriteLine($"{results.Count - failures}/{results.Count} working-directory/breadcrumb contracts passed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

async Task Run(string name, Func<Task> action)
{
    TestState.Reset();
    try { await action().WaitAsync(TimeSpan.FromSeconds(10)); results.Add(new { name, passed = true }); }
    catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.Message }); Console.Error.WriteLine(name + ": " + ex.Message); }
}
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
static Task<IReadOnlyList<PathBoxItem>> Components(string path) => Task.FromResult<IReadOnlyList<PathBoxItem>>([new() { Path = path, Title = path }]);
static BaseShellPage PageWithInitialBreadcrumb()
{
    var page = new BaseShellPage();
    page.ToolbarViewModel.PathComponents.Add(new() { Path = "A", Title = "A" });
    return page;
}
