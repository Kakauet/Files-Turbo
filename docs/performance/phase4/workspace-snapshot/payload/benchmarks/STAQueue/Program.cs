using Files.App.Storage;
using System.Diagnostics;
using System.Text.Json;

var variant = args[0];
var output = Path.GetFullPath(args[1]);
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output outside workspace.");
Directory.CreateDirectory(output);
var results = new List<object>();
for (var round = 0; round < 5; round++)
{
    using var started = new CountdownEvent(12);
    using var gate = new ManualResetEventSlim();
    var blockers = Enumerable.Range(0, 12).Select(_ => STATask.RunPooled(() =>
    {
        started.Signal();
        if (!gate.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("STA blocker timeout.");
        return Thread.CurrentThread.GetApartmentState();
    }, null)).ToArray();
    if (!started.Wait(TimeSpan.FromSeconds(20))) { gate.Set(); throw new TimeoutException("STA pool startup timeout."); }
    using var cancellation = new CancellationTokenSource();
    var called = 0;
    Task<int> Enqueue() =>
#if PHASE2
        STATask.RunPooled(() => Interlocked.Increment(ref called), null, cancellation.Token);
#else
        STATask.RunPooled(() => Interlocked.Increment(ref called), null);
#endif
    var obsolete = Enumerable.Range(0, 1000).Select(_ => Enqueue()).ToArray();
    var watch = Stopwatch.StartNew();
    cancellation.Cancel();
    var cancelledWhileBlocked = obsolete.Count(task => task.IsCanceled);
    var cancelMs = watch.Elapsed.TotalMilliseconds;
    gate.Set();
    var apartments = await Task.WhenAll(blockers).WaitAsync(TimeSpan.FromSeconds(20));
    foreach (var task in obsolete)
        try { await task.WaitAsync(TimeSpan.FromSeconds(20)); } catch (OperationCanceledException) { }
    var sentinel = await STATask.RunPooled(() => 7, null).WaitAsync(TimeSpan.FromSeconds(20));
    if (apartments.Any(x => x != ApartmentState.STA) || sentinel != 7) throw new Exception("Pool apartment or liveness failed.");
    if (variant == "phase2" && (called != 0 || cancelledWhileBlocked != 1000)) throw new Exception("Obsolete queued work executed or didn't cancel promptly.");
    results.Add(new { test = "saturated_pool_cancellation", round, jobs = 1000, called, cancelledWhileBlocked, cancelMs, passed = true, scope = "actual STA pool/apartment; synthetic delegates, no shell or UI" });
}

#if PHASE2
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var called = false;
    var task = STATask.RunPooled(() => { called = true; return 1; }, null, cancellation.Token);
    try { await task; throw new Exception("Precancel failed."); } catch (OperationCanceledException) { }
    if (called) throw new Exception("Precancel ran delegate.");
    results.Add(new { test = "pre_cancel", passed = true });
}
{
    using var started = new ManualResetEventSlim();
    using var gate = new ManualResetEventSlim();
    using var cancellation = new CancellationTokenSource();
    var task = STATask.RunPooled(() => { started.Set(); gate.Wait(TimeSpan.FromSeconds(20)); return 99; }, null, cancellation.Token);
    if (!started.Wait(TimeSpan.FromSeconds(20))) { gate.Set(); throw new TimeoutException(); }
    cancellation.Cancel();
    try { await task.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Running cancellation failed."); } catch (OperationCanceledException) { }
    gate.Set();
    if (await STATask.RunPooled(() => 7, null).WaitAsync(TimeSpan.FromSeconds(20)) != 7) throw new Exception("Pool stopped.");
    results.Add(new { test = "cancel_running_native_delegate_result", passed = true, nativeWorkInterruptible = false });
}
var failure = await STATask.RunPooled<int>(() => throw new IOException("Synthetic failure"), null);
if (failure != 0 || await STATask.RunPooled(() => 8, null) != 8) throw new Exception("Failure recovery changed.");
results.Add(new { test = "exception_recovery", passed = true });
#endif
File.WriteAllText(Path.Combine(output, $"{variant}.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{variant}: {results.Count} STA queue cases passed.");
