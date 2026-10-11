using Files.App.Views.Layouts;
using System.Security.Cryptography;
using System.Text.Json;

var taskWorkspace=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
using var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(taskWorkspace,"tests-extra/Phase4ViewportIntegration/source-metadata.json")));
VerifySources();
var results=new List<object>();
Run("metadata_failure_retries_same_row_without_duplicate_thumbnail",()=>
{
    var item=new ListedItem(); var shell=new ShellViewModel();
    shell.MetadataProvider=(row,token)=>row.MetadataCalls==1?Task.FromException(new IOException("Synthetic one-time failure.")):Task.CompletedTask;
    var page=new BaseLayoutPage(shell); page.Track(item); page.Start(); page.DispatcherQueue.Drain();
    Assert(item.ItemPropertiesInitialized&&item.MetadataCalls==2&&item.Thumbnails==1,"Metadata did not retry or duplicate thumbnail extraction."); page.Dispose();
});
Run("metadata_capacity_rejection_refills_without_duplicate_thumbnails",()=>
{
    var rows=Enumerable.Range(0,150).Select(_=>new ListedItem()).ToArray();
    var hold=new TaskCompletionSource(); var shell=new ShellViewModel { MetadataProvider=(_,_)=>hold.Task };
    var page=new BaseLayoutPage(shell); foreach(var row in rows) page.Track(row); page.Start(); page.DispatcherQueue.Drain();
    Assert(page.MetadataPending<=128&&page.MetadataPending>0,"Metadata queue lost its bound or test did not create pending work.");
    hold.SetResult(); page.DispatcherQueue.Drain();
    Assert(rows.All(row=>row.ItemPropertiesInitialized),"Evicted metadata rows stayed incomplete.");
    Assert(page.FailedRows==0,"Pending metadata eviction was counted as an executed failure.");
    Assert(rows.All(row=>row.Thumbnails==1),"Metadata refill repeated a completed thumbnail stage."); page.Dispose();
});
Run("new_visible_metadata_evicts_pending_cached_row_without_reextracting_thumbnail",()=>
{
    var rows=Enumerable.Range(0,150).Select(_=>new ListedItem()).ToArray(); var hold=new TaskCompletionSource();
    var shell=new ShellViewModel {MetadataProvider=(_,_)=>hold.Task}; var page=new BaseLayoutPage(shell);
    foreach(var row in rows) page.Track(row); page.Start(); page.DispatcherQueue.Drain();
    Assert(page.MetadataPending==128,"Metadata setup did not reach its pending capacity.");
    page.SetVisibleRange(132,149); page.StartVisible(); page.DispatcherQueue.Drain();
    hold.SetResult(); page.DispatcherQueue.Drain();
    Assert(rows.Skip(132).All(row=>row.ItemPropertiesInitialized),"New visible rows did not displace cached metadata.");
    Assert(rows.Take(132).Any(row=>!row.ItemPropertiesInitialized),"No previously admitted cached metadata was evicted.");
    Assert(page.FailedRows==0,"Eviction consumed an executed failure attempt.");
    page.SetVisibleRange(-1,-1); page.StartVisible(); page.DispatcherQueue.Drain();
    Assert(rows.All(row=>row.ItemPropertiesInitialized&&row.Thumbnails==1),"Evicted metadata did not recover or repeated thumbnail extraction."); page.Dispose();
});
Run("invalidated_thumbnail_stage_reextracts_before_retrying_metadata",()=>
{
    var item=new ListedItem(); var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell); var container=page.Track(item);
    page.Start(); page.DispatcherQueue.Drain();
    item.Epoch++; item.ItemPropertiesInitialized=false;
    page.Retry(container,item); page.DispatcherQueue.Drain();
    Assert(item.Thumbnails==2&&item.ItemPropertiesInitialized,"Invalidated completed stage was coalesced and never refreshed."); page.Dispose();
});
Run("git_version_change_keeps_thumbnail_and_loads_new_git_properties",()=>
{
    var item=new GitItem(); var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell); var container=page.Track(item);
    page.Start(); page.DispatcherQueue.Drain(); shell.EnabledGitProperties=GitProperties.All;
    page.Retry(container,item); page.DispatcherQueue.Drain();
    Assert(item.Thumbnails==1&&item.StatusPropertiesInitialized&&item.CommitPropertiesInitialized,"Git version change lost metadata or repeated thumbnail."); page.Dispose();
});
Run("disposed_handoff_does_not_initialize_metadata",()=>
{
    var item=new ListedItem(); var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell); page.Track(item); page.Start(); page.Dispose(); page.DispatcherQueue.Drain();
    Assert(!item.ItemPropertiesInitialized&&item.Thumbnails==0&&page.CachedStages==0,"Disposed page executed queued stages.");
});
Run("visible_rows_start_in_vertical_order_despite_realization_order",()=>
{
    var rows=Enumerable.Range(0,12).Select(_=>new ListedItem()).ToArray();
    var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell);
    foreach(var index in new[] {9,1,7,0,11,3,8,2,5,10,4,6}) page.Track(rows[index],index);
    page.SetVisibleRange(2,9); page.StartVisible(); page.DispatcherQueue.Drain();
    Assert(shell.ThumbnailStartOrder.SequenceEqual(rows.Skip(2).Take(8)),"Visible thumbnail starts did not follow vertical indices.");
    Assert(rows.Take(2).Concat(rows.Skip(10)).All(row=>row.Thumbnails==0),"Initial visible pass admitted offscreen rows."); page.Dispose();
});
foreach(var (name,realizationOrder) in new[]
{
    ("phase_zero_without_phase_three_coalesces_reverse_callbacks_and_starts_top_four_first",Enumerable.Range(0,12).Reverse().ToArray()),
    ("phase_zero_without_phase_three_coalesces_shuffled_callbacks_and_starts_top_four_first",new[] {9,1,7,0,11,3,8,2,5,10,4,6})
})
Run(name,()=>
{
    var rows=Enumerable.Range(0,12).Select(_=>new ListedItem()).ToArray(); var hold=new TaskCompletionSource();
    var shell=new ShellViewModel {ThumbnailProvider=(_,_)=>hold.Task}; var page=new BaseLayoutPage(shell);
    try
    {
        page.SetVisibleRange(-1,-1);
        foreach(var index in realizationOrder) Assert(page.RealizePhaseZero(rows[index],index).RegisteredCallbacks==1,"Phase-zero test did not register the deferred phase-three fallback.");
        Assert(page.DispatcherQueue.PendingCount==1,"Initial callbacks did not schedule exactly one coalesced pass before a queue existed.");
        Assert(shell.ThumbnailStartOrder.Count==0,"Realization callbacks started thumbnail work before the sorted pass.");
        page.DispatcherQueue.Drain();
        Assert(shell.ThumbnailStartOrder.SequenceEqual(rows.Take(4)),"Missing visible range reserved initial four slots for lower rows.");
        Assert(rows.Skip(4).All(row=>row.Thumbnails==0),"More than four held thumbnail jobs started.");
        hold.SetResult(); page.DispatcherQueue.Drain();
        Assert(shell.ThumbnailStartOrder.SequenceEqual(rows),"Remaining thumbnail starts followed callback order instead of ascending container indices.");
        Assert(rows.All(row=>row.ItemPropertiesInitialized&&row.Thumbnails==1),"Sorted fallback lost a row or duplicated thumbnail work.");
    }
    finally {page.Dispose();}
});
foreach(var (name,realizationOrder) in new[]
{
    ("phase_zero_visible_range_loads_top_rows_from_reverse_callbacks",Enumerable.Range(0,12).Reverse().ToArray()),
    ("phase_zero_visible_range_loads_top_rows_from_shuffled_callbacks",new[] {9,1,7,0,11,3,8,2,5,10,4,6})
})
Run(name,()=>
{
    var rows=Enumerable.Range(0,12).Select(_=>new ListedItem()).ToArray(); var hold=new TaskCompletionSource();
    var shell=new ShellViewModel {ThumbnailProvider=(_,_)=>hold.Task}; var page=new BaseLayoutPage(shell);
    try
    {
        page.SetVisibleRange(2,9); foreach(var index in realizationOrder) page.RealizePhaseZero(rows[index],index);
        Assert(page.DispatcherQueue.NormalPending==1&&page.DispatcherQueue.LowPending==0,"Phase zero did not coalesce one Normal viewport pass.");
        page.DispatcherQueue.Drain();
        Assert(shell.ThumbnailStartOrder.SequenceEqual(rows.Skip(2).Take(4)),"Initial visible slots did not start at the top of the visible range.");
        hold.SetResult(); page.DispatcherQueue.Drain();
        Assert(shell.ThumbnailStartOrder.SequenceEqual(rows.Skip(2).Take(8)),"Visible phase-zero starts followed callback order.");
        Assert(rows.Take(2).Concat(rows.Skip(10)).All(row=>row.Thumbnails==0),"Phase-zero pass admitted offscreen rows.");
    }
    finally {page.Dispose();}
});
Run("phase_zero_pass_and_thumbnail_jobs_run_at_normal_before_low_metadata",()=>
{
    var rows=Enumerable.Range(0,8).Select(_=>new ListedItem()).ToArray(); var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell);
    try
    {
        var lowWorkRan=false; page.DispatcherQueue.EnqueueLow(()=>lowWorkRan=true);
        foreach(var index in Enumerable.Range(0,8).Reverse()) page.RealizePhaseZero(rows[index],index);
        page.DispatcherQueue.DrainNormal();
        Assert(shell.ThumbnailStartOrder.SequenceEqual(rows),"Normal-only dispatch did not execute sorted phase-zero image work.");
        Assert(!lowWorkRan&&rows.All(row=>row.MetadataCalls==0),"Low metadata or background work ran during the Normal-only pass.");
        Assert(page.DispatcherQueue.LowPending>0,"Metadata did not remain queued at Low priority.");
        page.DispatcherQueue.Drain(); Assert(lowWorkRan&&rows.All(row=>row.ItemPropertiesInitialized),"Low work failed to complete after image work.");
    }
    finally {page.Dispose();}
});
Run("hook_remembers_nonzero_position_and_extent_only_event_does_not_pause_initial_load",()=>
{
    var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell); var item=new ListedItem();
    try
    {
        var viewer=page.ItemsControl.ScrollViewer; viewer.HorizontalOffset=3; viewer.VerticalOffset=40; viewer.ZoomFactor=1.25f; page.HookScroll();
        page.RealizePhaseZero(item,0); viewer.ExtentHeight=10000; viewer.ViewportHeight=600; viewer.Raise(true);
        Assert(!shell.IsScrollInFlight&&shell.ScrollStateChanges.Count==0&&page.DispatcherQueue.Timers.Count==0,"Extent-only realization created a scroll pause or debounce timer.");
        page.DispatcherQueue.Drain(); Assert(item.Thumbnails==1&&item.ItemPropertiesInitialized,"Initial image load waited for a nonexistent scroll settle.");
    }
    finally {page.Dispose();}
});
foreach(var axis in new[] {"horizontal","vertical","zoom"})
Run("real_"+axis+"_movement_defers_phase_zero_until_settle",()=>
{
    var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell); var item=new ListedItem();
    try
    {
        page.HookScroll(); page.RealizePhaseZero(item,0); var viewer=page.ItemsControl.ScrollViewer;
        if(axis=="horizontal")viewer.HorizontalOffset=4; else if(axis=="vertical")viewer.VerticalOffset=40; else viewer.ZoomFactor=1.5f;
        viewer.Raise(true); page.DispatcherQueue.Drain();
        Assert(shell.IsScrollInFlight&&item.Thumbnails==0&&page.DispatcherQueue.Timers.Count==1,"Actual movement did not park initial image work.");
        Assert(page.DispatcherQueue.Timers[0].Interval==TimeSpan.FromMilliseconds(200),"Real movement changed the existing debounce interval.");
        page.FireScrollSettle(); page.DispatcherQueue.Drain();
        Assert(!shell.IsScrollInFlight&&item.ItemPropertiesInitialized&&item.Thumbnails==1,"Settle did not resume the deferred image.");
    }
    finally {page.Dispose();}
});
Run("extent_only_during_scroll_keeps_deadline_and_final_without_delta_still_settles",()=>
{
    var rows=Enumerable.Range(0,12).Select(_=>new ListedItem()).ToArray(); var hold=new TaskCompletionSource();
    var shell=new ShellViewModel {ThumbnailProvider=(_,_)=>hold.Task}; var page=new BaseLayoutPage(shell);
    try
    {
        page.HookScroll(); foreach(var index in Enumerable.Range(0,12).Reverse()) page.RealizePhaseZero(rows[index],index);
        page.SetVisibleRange(4,9); var viewer=page.ItemsControl.ScrollViewer; viewer.VerticalOffset=400; viewer.Raise(true); page.DispatcherQueue.Drain();
        var timer=page.DispatcherQueue.Timers.Single(); Assert(timer.Starts==1&&shell.ThumbnailStartOrder.Count==0,"Initial scroll did not defer the queued viewport pass.");
        viewer.ExtentHeight=100000; viewer.ViewportHeight=600; viewer.Raise(true);
        Assert(timer.Starts==1&&shell.ScrollStateChanges.SequenceEqual(new[] {true}),"Intermediate geometry-only event postponed the existing deadline.");
        viewer.Raise(false); Assert(timer.Starts==2&&timer.IsRunning,"Final event repeating the offset lost its settle opportunity.");
        page.FireScrollSettle(); page.DispatcherQueue.Drain();
        Assert(!shell.IsScrollInFlight&&shell.ThumbnailStartOrder.SequenceEqual(rows.Skip(4).Take(4)),"Settle did not reserve first four slots for the current visible top rows.");
        hold.SetResult(); page.DispatcherQueue.Drain();
        Assert(shell.ThumbnailStartOrder.SequenceEqual(rows.Skip(4).Take(6)),"After settle, remaining visible jobs started out of order.");
        Assert(shell.ScrollStateChanges.SequenceEqual(new[] {true,false}),"Geometry updates toggled the scroll state unnecessarily.");
    }
    finally {page.Dispose();}
});
Run("final_without_movement_at_initial_position_does_not_create_debounce",()=>
{
    var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell); var item=new ListedItem();
    try
    {
        page.HookScroll(); page.RealizePhaseZero(item,0); page.ItemsControl.ScrollViewer.Raise(false); page.DispatcherQueue.Drain();
        Assert(!shell.IsScrollInFlight&&page.DispatcherQueue.Timers.Count==0&&item.Thumbnails==1,"Idle final geometry event introduced a 200-ms wait.");
    }
    finally {page.Dispose();}
});
Run("disposing_during_scroll_stops_timer_and_unhooks_geometry_events",()=>
{
    var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell); var item=new ListedItem();
    page.HookScroll(); page.RealizePhaseZero(item,0); var viewer=page.ItemsControl.ScrollViewer; viewer.VerticalOffset=40; viewer.Raise(true);
    var timer=page.DispatcherQueue.Timers.Single(); page.Dispose(); timer.Fire(); viewer.VerticalOffset=80; viewer.Raise(false); page.DispatcherQueue.Drain();
    Assert(!shell.IsScrollInFlight&&!timer.IsRunning&&item.Thumbnails==0&&page.CachedStages==0,"Disposed scroll lifecycle executed queued or delayed thumbnail work.");
});
Run("scroll_settle_resets_retry_budget_and_geometry_only_does_not",()=>
{
    var item=new ListedItem(); var shell=new ShellViewModel {ThumbnailProvider=(_,_)=>Task.FromException(new IOException("Synthetic persistent failure."))}; var page=new BaseLayoutPage(shell);
    try
    {
        page.HookScroll(); page.RealizePhaseZero(item,0); page.DispatcherQueue.Drain(200); Assert(item.Thumbnails==2,"Initial retries were not bounded.");
        var viewer=page.ItemsControl.ScrollViewer; viewer.ExtentHeight=10000; viewer.Raise(false); page.DispatcherQueue.Drain(200);
        Assert(item.Thumbnails==2,"Geometry-only event reset the failure budget.");
        viewer.VerticalOffset=1; viewer.Raise(true); page.FireScrollSettle(); page.DispatcherQueue.Drain(200);
        Assert(item.Thumbnails==4,"Actual scroll settle failed to allow exactly one bounded new retry sequence.");
    }
    finally {page.Dispose();}
});
Run("phase_zero_and_scroll_diagnostics_are_opt_in",()=>
{
    Files.App.Helpers.UiPerformanceTrace.Records.Clear(); var shell=new ShellViewModel {PerformanceOperationId=77}; var page=new BaseLayoutPage(shell);
    try
    {
        page.HookScroll(); page.RealizePhaseZero(new(),0); page.ItemsControl.ScrollViewer.Raise(false); page.DispatcherQueue.Drain();
        Assert(Files.App.Helpers.UiPerformanceTrace.Records.Count==0,"Disabled diagnostics created scheduler markers.");
        Files.App.Helpers.UiPerformanceTrace.IsEnabled=true; page.RealizePhaseZero(new(),1); page.DispatcherQueue.Drain();
        var viewer=page.ItemsControl.ScrollViewer; viewer.Raise(false); viewer.VerticalOffset=5; viewer.Raise(true); viewer.Raise(false); page.FireScrollSettle(); page.DispatcherQueue.Drain();
        var markers=Files.App.Helpers.UiPerformanceTrace.Records.Select(row=>row.Milestone).ToHashSet();
        Assert(new[] {"ViewportWorkRequested","ViewportPassQueued","ViewportPassStarted","ViewportPassCompleted","ScrollGeometryIgnored","ScrollDeferred","ScrollFinalWithoutMovement","ScrollSettleScheduled","ScrollSettled"}.All(markers.Contains),"Enabled diagnosis did not record the phase-zero/pass/scroll lifecycle.");
        Assert(Files.App.Helpers.UiPerformanceTrace.Records.All(row=>row.Operation==77&&row.DurationMs>=0),"Scheduler marker lost its operation identity or duration validity.");
    }
    finally {Files.App.Helpers.UiPerformanceTrace.IsEnabled=false; page.Dispose();}
});
Run("reload_prioritizes_vertical_visible_indices_before_cached_rows",()=>
{
    var rows=Enumerable.Range(0,12).Select(_=>new ListedItem()).ToArray(); var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell);
    foreach(var index in new[] {9,1,7,0,11,3,8,2,5,10,4,6}) page.Track(rows[index],index);
    page.SetVisibleRange(2,9); page.StartVisible(); page.DispatcherQueue.Drain(); shell.ThumbnailStartOrder.Clear();
    page.Reload().GetAwaiter().GetResult(); page.DispatcherQueue.Drain();
    Assert(shell.ThumbnailStartOrder.Take(8).SequenceEqual(rows.Skip(2).Take(8)),"Reload admitted cached rows before visible vertical priorities.");
    Assert(rows.All(row=>row.ItemPropertiesInitialized),"Reload lost a realized row."); page.Dispose();
});
Run("reload_before_queue_creation_keeps_first_slots_for_visible_top_rows",()=>
{
    var rows=Enumerable.Range(0,12).Select(_=>new ListedItem()).ToArray(); var shell=new ShellViewModel(); var page=new BaseLayoutPage(shell);
    foreach(var index in new[] {9,1,7,0,11,3,8,2,5,10,4,6}) page.Track(rows[index],index);
    page.SetVisibleRange(2,9); page.Reload().GetAwaiter().GetResult(); page.DispatcherQueue.Drain();
    Assert(shell.ThumbnailStartOrder.Take(8).SequenceEqual(rows.Skip(2).Take(8)),"Early reload reserved initial slots for cached rows.");
    Assert(rows.All(row=>row.ItemPropertiesInitialized),"Early reload lost a realized row."); page.Dispose();
});
Run("persistent_metadata_failure_has_bounded_immediate_retries",()=>
{
    var item=new ListedItem(); var shell=new ShellViewModel {MetadataProvider=(_,_)=>Task.FromException(new IOException("Synthetic persistent failure."))};
    var page=new BaseLayoutPage(shell); page.Track(item); page.Start();
    try {page.DispatcherQueue.Drain(200); Assert(item.MetadataCalls==2&&page.FailedRows==1,"Persistent failure did not stop after one immediate retry.");}
    finally {page.Dispose();}
});
Run("leaving_viewport_clears_failure_bound_and_allows_new_attempt",()=>
{
    var item=new ListedItem(); var shell=new ShellViewModel {MetadataProvider=(_,_)=>Task.FromException(new IOException("Synthetic persistent failure."))};
    var page=new BaseLayoutPage(shell); page.Track(item,0); page.SetVisibleRange(0,0); page.StartVisible(); page.DispatcherQueue.Drain();
    Assert(item.MetadataCalls==2&&page.FailedRows==1,"Persistent failure was not retained under the bounded retry policy.");
    page.SetVisibleRange(1,1); page.Reprioritize(); page.DispatcherQueue.Drain();
    Assert(page.FailedRows==0&&page.CachedStages==0,"Offscreen failed row retained a handoff or failure entry.");
    shell.MetadataProvider=(_,_)=>Task.CompletedTask; page.SetVisibleRange(0,0); page.StartVisible(); page.DispatcherQueue.Drain();
    Assert(item.ItemPropertiesInitialized&&item.MetadataCalls==3&&item.Thumbnails==2,"Returning visible row was blocked by its previous failure count."); page.Dispose();
});
Run("persistent_thumbnail_failure_has_bounded_immediate_retries",()=>
{
    var item=new ListedItem(); var shell=new ShellViewModel {ThumbnailProvider=(_,_)=>Task.FromException(new IOException("Synthetic persistent thumbnail failure."))};
    var page=new BaseLayoutPage(shell); page.Track(item); page.Start();
    try {page.DispatcherQueue.Drain(200); Assert(item.Thumbnails==2&&item.MetadataCalls==0,"Persistent thumbnail failure did not stop after one immediate retry.");}
    finally {page.Dispose();}
});
VerifySources();
var output=Path.GetFullPath(args.FirstOrDefault()??Path.Combine(taskWorkspace,"reports/phase4-viewport-integration-contracts.json")); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output,JsonSerializer.Serialize(new {Source=metadata.RootElement.Clone(),Results=results,Passed=results.Count(row=>JsonSerializer.Serialize(row).Contains("\"Passed\":true",StringComparison.Ordinal)),Scope="Exact handoff methods/queue, controlled Shell/dispatcher/viewport. No WinUI or native latency."},new JsonSerializerOptions {WriteIndented=true}));
Console.WriteLine($"Completed {results.Count} integration contracts. {output}");
return results.All(row=>JsonSerializer.Serialize(row).Contains("\"Passed\":true",StringComparison.Ordinal))?0:1;

void Run(string name,Action action) { try {action(); results.Add(new {Name=name,Passed=true,Failure=(string?)null}); Console.WriteLine($"PASS {name}");} catch(Exception error) {results.Add(new {Name=name,Passed=false,Failure=error.Message}); Console.WriteLine($"FAIL {name}: {error.Message}");} }
static void Assert(bool condition,string message) {if(!condition)throw new Exception(message);}
void VerifySources()
{
    foreach(var (pathKey,hashKey) in new[] {("SourcePath","SourceSha256"),("QueuePath","QueueSha256")})
        VerifyHash(metadata.RootElement.GetProperty(pathKey).GetString()!,metadata.RootElement.GetProperty(hashKey).GetString()!);
    VerifyHash(Path.Combine(taskWorkspace,"tests-extra/Phase4ViewportIntegration/ViewportCore.generated.cs"),metadata.RootElement.GetProperty("GeneratedSha256").GetString()!);
}
static void VerifyHash(string path,string expected) {if(!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException($"Source changed during test: {path}");}
