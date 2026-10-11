using Files.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Program
{
    private static readonly TimeSpan ContractTimeout = TimeSpan.FromSeconds(10);
    private sealed record ContractResult(string Name, bool Passed, string Detail);
    private sealed record DecodeCase(string Name, uint Size, float Dpi, byte[] Data, int Width, int Height, bool Sync);

    public static async Task<int> Main(string[] args)
    {
        var workspace = FindWorkspace();
        var metadataPath = Path.Combine(workspace, "tests-extra", "Phase4IconCache", "source-metadata.json");
        using var metadataDocument = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var source = metadataDocument.RootElement.Clone();
        ValidateSources(source);
        List<ContractResult> contracts = [];
        DecodeCase[] decodeCases =
        [
            new("DPI 125% decodes physical 80px with bounded synchronous source", 64, 1.25f, TinyCodec.Png(64, 64), 80, 0, true),
            new("fractional physical size rounds upward", 33, 1.25f, TinyCodec.Png(64, 64), 42, 0, true),
            new("zero logical size requests minimum one physical decode pixel", 0, 1f, TinyCodec.Png(64, 64), 1, 0, true),
            new("portrait constrains height and preserves width ratio", 48, 1.5f, TinyCodec.Png(32, 64), 0, 72, true),
            new("physical target 256px remains synchronously bounded", 256, 1f, TinyCodec.Png(256, 256), 256, 0, true),
            new("physical target above 256px remains asynchronous", 256, 1.001f, TinyCodec.Png(64, 64), 257, 0, false),
            new("source width above 256px remains asynchronous", 64, 1f, TinyCodec.Png(257, 64), 64, 0, false),
            new("source height above 256px remains asynchronous", 64, 1f, TinyCodec.Png(64, 257), 0, 64, false),
            new("encoded data exactly 256KiB remains synchronously bounded", 64, 1f, TinyCodec.Png(64, 64, 256 * 1024), 64, 0, true),
            new("encoded data above 256KiB remains asynchronous", 64, 1f, TinyCodec.Png(64, 64, 256 * 1024 + 1), 64, 0, false),
            new("unrecognized encoded format keeps asynchronous fallback", 64, 1f, [1, 2, 3, 4], 64, 0, false)
        ];
        foreach (var expected in decodeCases)
        {
            await Check(expected.Name, async () =>
            {
                var cache = Fresh();
                global::Files.App.App.AppModel.AppWindowDPI = expected.Dpi;
                FileThumbnailHelper.Handler = (_, _, _) => Task.FromResult<byte[]?>(expected.Data);
                var image = await cache.GetIconImageAsync("synthetic-a.png", ".png", false, expected.Size);
                var decode = OneDecode();
                Require(image is not null && decode.Width == expected.Width && decode.Height == expected.Height &&
                    decode.PixelType == DecodePixelType.Physical && decode.Synchronous == expected.Sync);
                Require(decode.EncodedBytes == expected.Data.Length && FileThumbnailHelper.Requests.Single().Size == expected.Size,
                    "Shell receives logical size; service decode applies DPI once.");
            });
        }
        await Check("same extension case and different paths share one decoded image", async () =>
        {
            var cache = Fresh();
            var first = await cache.GetIconImageAsync("synthetic-a.PNG", ".PNG", false, 64);
            var second = await cache.GetIconImageAsync("synthetic-b.png", ".png", false, 64);
            Require(first is not null && ReferenceEquals(first, second) && FileThumbnailHelper.Calls == 1 && TinyCodec.Records.Count == 1);
            Require(!FileThumbnailHelper.Requests.Single().Path!.Contains("synthetic-", StringComparison.Ordinal));
        });
        await Check("logical sizes partition shared cache and retain icon-only native request", async () =>
        {
            var cache = Fresh();
            var first = await cache.GetIconImageAsync(null, ".png", false, 32);
            var second = await cache.GetIconImageAsync(null, ".png", false, 64);
            Require(first is not null && second is not null && !ReferenceEquals(first, second));
            Require(FileThumbnailHelper.Calls == 2 && TinyCodec.Records.Select(x => x.Width).SequenceEqual(new[] { 32, 64 }));
            Require(FileThumbnailHelper.Requests.All(x => x.Options == IconOptions.ReturnIconOnly));
        });
        await Check("32 pending byte requests coalesce one native load", async () =>
        {
            var cache = Fresh();
            var gate = NewGate<byte[]?>();
            FileThumbnailHelper.Handler = (_, _, _) => gate.Task;
            var tasks = Enumerable.Range(0, 32).Select(_ => cache.GetIconAsync(null, ".png", false, 64)).ToArray();
            Require(FileThumbnailHelper.Calls == 1);
            var bytes = TinyCodec.Png(64, 64);
            gate.SetResult(bytes);
            var results = await Task.WhenAll(tasks).WaitAsync(ContractTimeout);
            Require(results.All(x => ReferenceEquals(x, bytes)) && TinyCodec.Records.IsEmpty);
        });
        await Check("32 pending image requests coalesce one decode and one bitmap", async () =>
        {
            var cache = Fresh();
            var gate = NewGate();
            FileThumbnailHelper.Handler = (_, _, _) => Task.FromResult<byte[]?>(TinyCodec.Png(257, 64));
            TinyCodec.AsyncHandler = _ => gate.Task;
            var tasks = Enumerable.Range(0, 32).Select(_ => cache.GetIconImageAsync(null, ".png", false, 64)).ToArray();
            Require(FileThumbnailHelper.Calls == 1 && TinyCodec.Records.Count == 1 && tasks.All(x => !x.IsCompleted));
            gate.SetResult();
            var images = await Task.WhenAll(tasks).WaitAsync(ContractTimeout);
            Require(images[0] is not null && images.All(x => ReferenceEquals(x, images[0])));
        });
        await Check("Clear replaces completed byte and image cache generations", async () =>
        {
            var cache = Fresh();
            var first = await cache.GetIconImageAsync(null, ".png", false, 64);
            cache.Clear();
            var second = await cache.GetIconImageAsync(null, ".png", false, 64);
            Require(first is not null && second is not null && !ReferenceEquals(first, second));
            Require(FileThumbnailHelper.Calls == 2 && TinyCodec.Records.Count == 2);
        });
        await Check("Clear during pending bytes cannot repopulate current image generation", async () =>
        {
            var cache = Fresh();
            var oldGate = NewGate<byte[]?>();
            FileThumbnailHelper.Handler = (_, _, _) => FileThumbnailHelper.Calls == 1
                ? oldGate.Task : Task.FromResult<byte[]?>(TinyCodec.Png(32, 32));
            var old = cache.GetIconImageAsync(null, ".png", false, 64);
            cache.Clear();
            var current = await cache.GetIconImageAsync(null, ".png", false, 64);
            oldGate.SetResult(TinyCodec.Png(64, 64));
            var oldImage = await old.WaitAsync(ContractTimeout);
            var reused = await cache.GetIconImageAsync(null, ".png", false, 64);
            Require(current is not null && oldImage is not null && !ReferenceEquals(current, oldImage) && ReferenceEquals(current, reused));
            Require(FileThumbnailHelper.Calls == 2 && TinyCodec.Records.Count == 2);
        });
        await Check("Clear during pending decode isolates completed old bitmap", async () =>
        {
            var cache = Fresh();
            var oldGate = NewGate();
            FileThumbnailHelper.Handler = (_, _, _) => Task.FromResult<byte[]?>(TinyCodec.Png(257, 64));
            TinyCodec.AsyncHandler = _ => TinyCodec.Records.Count == 1 ? oldGate.Task : Task.CompletedTask;
            var old = cache.GetIconImageAsync(null, ".png", false, 64);
            cache.Clear();
            var current = await cache.GetIconImageAsync(null, ".png", false, 64);
            oldGate.SetResult();
            var oldImage = await old.WaitAsync(ContractTimeout);
            var reused = await cache.GetIconImageAsync(null, ".png", false, 64);
            Require(current is not null && oldImage is not null && !ReferenceEquals(current, oldImage) && ReferenceEquals(current, reused));
            Require(FileThumbnailHelper.Calls == 2 && TinyCodec.Records.Count == 2);
        });
        await Check("old-generation decode failure cannot remove newer cached bitmap", async () =>
        {
            var cache = Fresh();
            var oldGate = NewGate();
            FileThumbnailHelper.Handler = (_, _, _) => Task.FromResult<byte[]?>(TinyCodec.Png(257, 64));
            TinyCodec.AsyncHandler = _ => TinyCodec.Records.Count == 1 ? oldGate.Task : Task.CompletedTask;
            var old = cache.GetIconImageAsync(null, ".png", false, 64);
            cache.Clear();
            var current = await cache.GetIconImageAsync(null, ".png", false, 64);
            oldGate.SetException(new IOException("Synthetic old-generation decoder fault."));
            Require(await old.WaitAsync(ContractTimeout) is null);
            var reused = await cache.GetIconImageAsync(null, ".png", false, 64);
            Require(current is not null && ReferenceEquals(current, reused) && FileThumbnailHelper.Calls == 2 && TinyCodec.Records.Count == 2);
        });
        await Check("null decode result removes image entry and retries existing bytes", async () =>
        {
            var cache = Fresh();
            TinyCodec.SyncHandler = _ =>
            {
                if (TinyCodec.Records.Count == 1)
                    throw new IOException("Synthetic codec rejection returns null through literal BitmapHelper.");
            };
            Require(await cache.GetIconImageAsync(null, ".png", false, 64) is null);
            var retried = await cache.GetIconImageAsync(null, ".png", false, 64);
            var reused = await cache.GetIconImageAsync(null, ".png", false, 64);
            Require(retried is not null && ReferenceEquals(retried, reused) && TinyCodec.Records.Count == 2 && FileThumbnailHelper.Calls == 1);
        });
        await Check("native fault removes both image and byte entries for next request", async () =>
        {
            var cache = Fresh();
            FileThumbnailHelper.Handler = (_, _, _) => FileThumbnailHelper.Calls == 1
                ? Task.FromException<byte[]?>(new IOException("Synthetic provider fault."))
                : Task.FromResult<byte[]?>(TinyCodec.Png(64, 64));
            var threw = false;
            try { await cache.GetIconImageAsync(null, ".png", false, 64); }
            catch (IOException) { threw = true; }
            var retried = await cache.GetIconImageAsync(null, ".png", false, 64);
            Require(threw && retried is not null && FileThumbnailHelper.Calls == 2 && TinyCodec.Records.Count == 1);
        });
        await Check("null native bytes retain original byte-cache behavior without decoding", async () =>
        {
            var cache = Fresh();
            FileThumbnailHelper.Handler = (_, _, _) => Task.FromResult<byte[]?>(null);
            Require(await cache.GetIconImageAsync(null, ".png", false, 64) is null);
            Require(await cache.GetIconImageAsync(null, ".png", false, 64) is null);
            Require(FileThumbnailHelper.Calls == 1 && TinyCodec.Records.IsEmpty);
        });
        await Check("off-UI entry returns null before cold provider or decoder work", async () =>
        {
            var cache = Fresh();
            DispatcherQueue.Enabled = false;
            try
            {
                var image = await Task.Run(() => cache.GetIconImageAsync(null, ".png", false, 64));
                Require(image is null && FileThumbnailHelper.Calls == 0 && TinyCodec.Records.IsEmpty);
            }
            finally { DispatcherQueue.Enabled = true; }
        });
        await Check("off-UI entry cannot expose an already cached bitmap", async () =>
        {
            var cache = Fresh();
            var current = await cache.GetIconImageAsync(null, ".png", false, 64);
            DispatcherQueue.Enabled = false;
            try { Require(await cache.GetIconImageAsync(null, ".png", false, 64) is null); }
            finally { DispatcherQueue.Enabled = true; }
            Require(current is not null && ReferenceEquals(current, await cache.GetIconImageAsync(null, ".png", false, 64)));
            Require(FileThumbnailHelper.Calls == 1 && TinyCodec.Records.Count == 1);
        });

        ValidateSources(source);
        var passed = contracts.Count(x => x.Passed);
        var reportPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(workspace, "reports", "phase4-icon-cache-contracts.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report = new
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Source = source,
            Scope = "Full production IconCacheService linked directly; literal BitmapHelper methods with synthetic byte/codec/dispatcher doubles. No WinUI rendering or native provider performance claim.",
            Total = contracts.Count,
            Passed = passed,
            Contracts = contracts
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n").Replace("\n", "\r\n");
        File.WriteAllText(reportPath, json + "\r\n");
        Console.WriteLine($"IconCache contracts: {passed}/{contracts.Count}. Report: {reportPath}");
        return passed == contracts.Count && contracts.Count == 24 ? 0 : 1;

        async Task Check(string name, Func<Task> body)
        {
            try
            {
                await body().WaitAsync(ContractTimeout);
                contracts.Add(new(name, true, ""));
            }
            catch (Exception ex)
            {
                contracts.Add(new(name, false, ex.ToString()));
            }
        }
    }

    private static IconCacheService Fresh()
    {
        FileThumbnailHelper.Reset();
        return new();
    }

    private static DecodeRecord OneDecode()
    {
        Require(TinyCodec.Records.Count == 1, $"Expected one decode, observed {TinyCodec.Records.Count}.");
        return TinyCodec.Records.Single();
    }

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<T> NewGate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Require(bool condition, string message = "Contract assertion failed.")
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static string FindWorkspace()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "source")) && Directory.Exists(Path.Combine(directory.FullName, "tests-extra")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Workspace root not found above contract executable.");
    }

    private static void ValidateSources(JsonElement source)
    {
        Require(source.GetProperty("IconSourceSha256").GetString() == IconCacheBuildStamp.IconSourceSha256 &&
            source.GetProperty("BitmapSourceSha256").GetString() == IconCacheBuildStamp.BitmapSourceSha256 &&
            source.GetProperty("SourceRevision").GetString() == IconCacheBuildStamp.SourceRevision,
            "Compiled source stamp differs from current preparation; rebuild after preparation.");
        foreach (var pair in new[] { ("IconSourcePath", "IconSourceSha256"), ("BitmapSourcePath", "BitmapSourceSha256"), ("GeneratedPath", "GeneratedSha256") })
        {
            var path = source.GetProperty(pair.Item1).GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Require(actual == source.GetProperty(pair.Item2).GetString(), $"Source/preparation hash changed: {path}");
        }
    }
}
