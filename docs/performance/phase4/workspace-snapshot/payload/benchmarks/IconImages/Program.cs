using Files.App.Services;
using Files.App.Utils.Storage;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text.Json;
using System.Runtime.CompilerServices;

var variant = args[0];
var output = Path.GetFullPath(args[1]);
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Output must stay in workspace.");
Directory.CreateDirectory(output);
var results = new List<object>();
foreach (var count in new[] { 1, 100, 1000 })
{
    BitmapExtensions.Reset();
    var cache = new IconCacheService();
    var gate = new TaskCompletionSource<BitmapImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
    BitmapExtensions.Handler = () => gate.Task;
    var requests = Enumerable.Range(0, count).Select(_ => cache.GetIconImageAsync(null, ".txt", false, 32)).ToArray();
    var conversions = BitmapExtensions.Calls;
    gate.SetResult(new BitmapImage());
    var images = await Task.WhenAll(requests);
    if (images.Any(image => image is null)) throw new Exception("Missing image.");
    if (variant == "phase2" && conversions != 1) throw new Exception("Duplicate decode.");
    results.Add(new { test = "same_key_pending_decode", count, conversions, passed = true, scope = "real cache source; WinUI decode double, no UI timing" });
}

BitmapExtensions.Reset();
{
    var cache = new IconCacheService();
    var gate = new TaskCompletionSource<BitmapImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
    BitmapExtensions.Handler = () => BitmapExtensions.Calls == 1 ? gate.Task : Task.FromResult<BitmapImage?>(new BitmapImage());
    var oldRequest = cache.GetIconImageAsync(null, ".txt", false, 32);
    cache.Clear();
    var oldImage = new BitmapImage();
    gate.SetResult(oldImage);
    await oldRequest;
    var currentImage = await cache.GetIconImageAsync(null, ".txt", false, 32);
    var stale = ReferenceEquals(currentImage, oldImage);
    if (variant == "phase2" && stale) throw new Exception("Clear repopulated stale bitmap.");
    results.Add(new { test = "clear_pending_decode", stale, passed = true, diagnostic = variant == "phase1" });
}

BitmapExtensions.Reset();
{
    var cache = new IconCacheService();
    BitmapExtensions.Handler = () => BitmapExtensions.Calls == 1 ? Task.FromException<BitmapImage?>(new IOException("Synthetic decode failure")) : Task.FromResult<BitmapImage?>(new BitmapImage());
    try { await cache.GetIconImageAsync(null, ".txt", false, 32); throw new Exception("Failure swallowed."); } catch (IOException) { }
    if (await cache.GetIconImageAsync(null, ".txt", false, 32) is null || BitmapExtensions.Calls != 2) throw new Exception("Decode retry failed.");
    results.Add(new { test = "decode_failure_retry", passed = true });
}
BitmapExtensions.Reset();
{
    var cache = new IconCacheService();
    var references = PopulateThenClear(cache);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var retained = references.Count(reference => reference.TryGetTarget(out _));
    if (retained != 0) throw new Exception($"Clear retained {retained} completed images.");
    results.Add(new { test = "completed_images_released_after_clear", images = references.Length, retained, passed = true, scope = "managed reachability with image doubles; no WinUI RAM claim" });
}
File.WriteAllText(Path.Combine(output, $"{variant}.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{variant}: {results.Count} bitmap cache cases passed (phase1 defects recorded as diagnostics).");

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference<BitmapImage>[] PopulateThenClear(IconCacheService cache)
{
    var references = new WeakReference<BitmapImage>[1000];
    for (var index = 0; index < references.Length; index++)
        references[index] = new(cache.GetIconImageAsync(null, $".synthetic{index}", false, 32).GetAwaiter().GetResult()!);
    cache.Clear();
    return references;
}
