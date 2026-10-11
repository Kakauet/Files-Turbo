using Files.App.Utils.Storage;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

var taskWorkspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var metadataPath = Path.Combine(taskWorkspace, "tests-extra/Phase4SearchResource/source-metadata.json");
using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
VerifyHashes();
var results = new List<object>();

await Run("successful_tag_lookup_preserves_caller_ownership_and_data", async () =>
{
    var handle = new FindCloseSafeHandle();
    PInvoke.Lookup = path => { Assert(path == "workspace-synthetic-tag", "Wrong path opened."); return (handle, new WIN32_FIND_DATAW { Value = 42 }); };
    var result = await TagLookup.OpenAsync("workspace-synthetic-tag", CancellationToken.None);
    Assert(ReferenceEquals(result.Handle, handle) && result.Data.Value == 42 && handle.Disposals == 0, "Successful result was lost or disposed early.");
    result.Handle!.Dispose();
    Assert(handle.Disposals == 1, "Caller could not release the handle once.");
});

await Run("canceled_tag_lookup_returns_before_native_completion_and_releases_late_handle", async () =>
{
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var handle = new FindCloseSafeHandle();
    PInvoke.Lookup = _ => { entered.Set(); Assert(release.Wait(TimeSpan.FromSeconds(10)), "Native gate timed out."); return (handle, default); };
    using var cts = new CancellationTokenSource();
    var pending = TagLookup.OpenAsync("workspace-synthetic-tag", cts.Token);
    Assert(entered.Wait(TimeSpan.FromSeconds(5)), "Native task did not start.");
    try
    {
        cts.Cancel();
        var canceled = false;
        try { await pending.WaitAsync(TimeSpan.FromSeconds(1)); } catch (OperationCanceledException) { canceled = true; }
        Assert(canceled && handle.Disposals == 0, "Cancellation waited for native return or disposed an in-use resource.");
    }
    finally { release.Set(); }
    await handle.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(handle.Disposals == 1, "Late canceled handle was not released exactly once.");
});

await Run("timed_out_tag_lookup_returns_empty_and_releases_late_handle", async () =>
{
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var handle = new FindCloseSafeHandle();
    PInvoke.Lookup = _ => { entered.Set(); Assert(release.Wait(TimeSpan.FromSeconds(15)), "Native gate timed out."); return (handle, new WIN32_FIND_DATAW { Value = 7 }); };
    var pending = TagLookup.OpenAsync("workspace-synthetic-tag", CancellationToken.None);
    Assert(entered.Wait(TimeSpan.FromSeconds(5)), "Native task did not start.");
    try
    {
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(8));
        Assert(result.Handle is null && result.Data.Value == 0 && handle.Disposals == 0, "Timeout retained a result or disposed an in-use resource.");
    }
    finally { release.Set(); }
    await handle.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(handle.Disposals == 1, "Late timed out handle was not released exactly once.");
});

await Run("native_tag_lookup_failure_propagates_without_fabricated_result", async () =>
{
    PInvoke.Lookup = _ => throw new IOException("Synthetic lookup failure.");
    var failed = false;
    try { await TagLookup.OpenAsync("workspace-synthetic-tag", CancellationToken.None); } catch (IOException) { failed = true; }
    Assert(failed, "Native failure became a successful result.");
});

await Run("repeated_cancellation_does_not_leak_tag_handles", async () =>
{
    for (var iteration = 0; iteration < 20; iteration++)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var handle = new FindCloseSafeHandle();
        PInvoke.Lookup = _ => { entered.Set(); Assert(release.Wait(TimeSpan.FromSeconds(5)), "Native gate timed out."); return (handle, default); };
        using var cts = new CancellationTokenSource();
        var pending = TagLookup.OpenAsync("workspace-synthetic-tag", cts.Token);
        Assert(entered.Wait(TimeSpan.FromSeconds(5)), "Native task did not start.");
        cts.Cancel();
        try { await pending.WaitAsync(TimeSpan.FromSeconds(1)); } catch (OperationCanceledException) { }
        finally { release.Set(); }
        await handle.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(handle.Disposals == 1, "A repeated canceled request leaked or released twice.");
    }
});

VerifyHashes();
var outputPath = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(taskWorkspace, "reports/phase4-search-resource-contracts.json"));
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(new { Source = metadata.RootElement.Clone(), Results = results, Scope = "Production native lookup block with controlled native provider; no WinUI or actual tag database." }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Passed {results.Count}/5 source-linked resource contracts. {outputPath}");

async Task Run(string name, Func<Task> test)
{
    var stopwatch = Stopwatch.StartNew();
    await test();
    results.Add(new { Name = name, Passed = true, DurationMs = stopwatch.Elapsed.TotalMilliseconds });
}
void VerifyHashes()
{
    Assert(Hash(metadata.RootElement.GetProperty("SourcePath").GetString()!) == metadata.RootElement.GetProperty("SourceSha256").GetString(), "FolderSearch changed after extraction.");
    Assert(Hash(metadata.RootElement.GetProperty("HelperPath").GetString()!) == metadata.RootElement.GetProperty("HelperSha256").GetString(), "StorageEnumerationTask changed after extraction.");
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
