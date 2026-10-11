global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.IO;
global using Files.App;
global using Files.App.Data.Enums;
global using Files.App.Helpers;
global using Files.App.Storage;
global using Files.App.Utils.Storage;
global using Files.App.ViewModels;
global using Files.Shared.Helpers;
global using Microsoft.UI.Xaml.Media.Imaging;
global using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Microsoft.Extensions.Logging
{
	public interface ILogger { }
	public sealed class TestLogger : ILogger { }
	public static class LoggerExtensions
	{
		public static void LogWarning(this ILogger logger, string message, params object?[] args) { }
		public static void LogWarning(this ILogger logger, Exception error, string message) { }
	}
}
namespace Microsoft.UI.Xaml.Media.Imaging
{
	public enum DecodePixelType { Logical, Physical }
	public sealed class BitmapImage(int marker)
	{
		public int Marker { get; } = marker;
		public int DecodePixelWidth { get; set; }
		public int DecodePixelHeight { get; set; }
	}
}
namespace Microsoft.UI.Dispatching
{
	public enum DispatcherQueuePriority { Low, Normal }
	public sealed class DispatcherQueue
	{
		public ConcurrentQueue<DispatcherQueuePriority> Priorities { get; } = new();
		public Task EnqueueOrInvokeAsync(Func<Task> action, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
		{
			Priorities.Enqueue(priority);
			return action();
		}
		public Task<T> EnqueueOrInvokeAsync<T>(Func<Task<T>> action, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal)
		{
			Priorities.Enqueue(priority);
			return action();
		}
	}
}
namespace Files.App
{
	public static class App
	{
		public static AppModel AppModel { get; } = new();
		public static Microsoft.Extensions.Logging.ILogger Logger { get; } = new Microsoft.Extensions.Logging.TestLogger();
	}
	public sealed class AppModel { public float AppWindowDPI { get; set; } = 1; }
}
namespace Files.Shared.Helpers
{
	public static class FileExtensionHelpers
	{
		public static bool IsImageFile(string? extension) => extension is ".bmp" or ".png";
		public static bool IsExecutableFile(string? extension) => extension is ".exe";
		public static bool IsFontFile(string? extension) => false;
	}
	public static class PathHelpers { public static bool IsInSystemFontsFolder(string path) => false; }
}
namespace Files.App.Helpers
{
	public static class LayoutSizeKindHelper { public static uint GetIconSize(uint mode) => mode; }
	public static class FontFileHelper
	{
		public static Task<byte[]?> GetWinRTThumbnailAsync(string path, uint size) => Task.FromResult<byte[]?>(null);
		public static byte[]? GenerateFontThumbnail(string path, int size) => null;
	}
	public static class MtpHelpers { public static string? ResolveMtpShellPath(string path) => null; }
	public static partial class BitmapHelper
	{
		public static int Calls;
		public static ConcurrentQueue<(int Size, DecodePixelType PixelType, bool PreserveAspectRatio, bool SynchronousRequested, bool SynchronousSelected)> DecodeRequests = new();
		public static Func<byte[], Task<BitmapImage?>> Handler = data => Task.FromResult<BitmapImage?>(new(data[0]));
		public static Task<BitmapImage?> ToBitmapAsync(this byte[] data, int decodeSize = -1, DecodePixelType decodePixelType = DecodePixelType.Logical,
			bool preserveAspectRatio = false, bool useSynchronousSmallThumbnail = false)
		{
			Interlocked.Increment(ref Calls);
			DecodeRequests.Enqueue((decodeSize, decodePixelType, preserveAspectRatio, useSynchronousSmallThumbnail,
				ShouldUseSynchronousSmallThumbnail(data, decodeSize, decodePixelType, preserveAspectRatio, useSynchronousSmallThumbnail)));
			return Handler(data);
		}
		public static bool TestSynchronousSelection(byte[] data, int decodeSize = 96, DecodePixelType decodePixelType = DecodePixelType.Physical,
			bool preserveAspectRatio = true, bool useSynchronousSmallThumbnail = true)
			=> ShouldUseSynchronousSmallThumbnail(data, decodeSize, decodePixelType, preserveAspectRatio, useSynchronousSmallThumbnail);
		public static BitmapImage TestDecodeSize(byte[] data, int decodeSize, bool preserveAspectRatio)
		{
			var image = new BitmapImage(0);
			SetDecodeSize(image, data, decodeSize, preserveAspectRatio);
			return image;
		}
		public static void Reset() { Calls = 0; DecodeRequests.Clear(); Handler = data => Task.FromResult<BitmapImage?>(new(data[0])); }
	}
	public static class UiPerformanceTrace
	{
		public readonly record struct Record(string Milestone, double Duration, long Value);
		public static bool IsEnabled;
		public static string FixtureRoot = string.Empty;
		public static ConcurrentQueue<Record> Records = new();
		public static bool IsFixturePath(string? path) => path is not null && path.StartsWith(FixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		public static void Mark(long operationId, string milestone, int count = 0, double durationMs = 0, long value = 0)
		{
			if (IsEnabled && operationId != 0) Records.Enqueue(new(milestone, durationMs, value));
		}
	}
	public static class Win32Helper
	{
		public readonly record struct Request(string? Path, int Size, bool IsFolder, IconOptions Options, ApartmentState Apartment);
		public static ConcurrentQueue<Request> Requests = new();
		public static Func<Request, byte[]?> IconHandler = request => [(byte)request.Size];
		public static Func<string?, int, bool, byte[]?> OverlayHandler = (_, _, _) => null;
		public static int Overlays;
		public static byte[]? GetIcon(string? path, int size, bool folder, IconOptions options)
		{
			var request = new Request(path, size, folder, options, Thread.CurrentThread.GetApartmentState());
			Requests.Enqueue(request);
			return IconHandler(request);
		}
		public static byte[]? GetIconOverlay(string? path, int size, bool folder)
		{
			Interlocked.Increment(ref Overlays);
			return OverlayHandler(path, size, folder);
		}
		public static void Reset()
		{
			Requests = new(); Overlays = 0;
			IconHandler = request => [(byte)request.Size]; OverlayHandler = (_, _, _) => null;
		}
	}
}
namespace Files.App.ViewModels
{
	public sealed class ListedItem
	{
		public string? ItemPath { get; set; }
		public string? FileExtension { get; set; } = ".bmp";
		public bool IsFolder { get; set; }
		public bool IsShortcut { get; set; }
		public BitmapImage? FileImage { get; set; }
		public BitmapImage? IconOverlay { get; set; }
		public BitmapImage? ShieldIcon { get; set; }
		public byte[]? PreloadedIconData { get; set; }
		public bool NeedsDelayedThumbnailLoad { get; set; }
		public DateTimeOffset ItemDateModifiedReal { get; set; }
		public string GetRequiredPath() => ItemPath!;
	}
	public sealed class FolderSettings { public uint LayoutMode { get; set; } = 48; }
	public sealed class UserSettings { public FolderPreferences FoldersSettingsService { get; } = new(); }
	public sealed class FolderPreferences { public bool ShowThumbnails { get; set; } = true; }
	public sealed class IconCache
	{
		private readonly ConcurrentDictionary<uint, BitmapImage> images = new();
		public int ImageCalls;
		public Func<uint, Task<BitmapImage?>>? ImageHandler { get; set; }
		public Task<byte[]?> GetIconAsync(string? path, string? ext, bool folder, uint size) => Task.FromResult<byte[]?>([200]);
		public Task<BitmapImage?> GetIconImageAsync(string? path, string? ext, bool folder, uint size)
		{
			Interlocked.Increment(ref ImageCalls);
			return ImageHandler?.Invoke(size) ?? Task.FromResult<BitmapImage?>(images.GetOrAdd(size, _ => new(200)));
		}
	}
	public partial class ShellViewModel : IDisposable
	{
		private bool isDisposed;
		private readonly DispatcherQueue dispatcherQueue = new();
		private readonly IconCache iconCacheService = new();
		private readonly FolderSettings folderSettings = new();
		private readonly UserSettings UserSettingsService = new();
		private CancellationTokenSource loadPropsCTS = new();
		private readonly SemaphoreSlim loadThumbnailSemaphore = new(8, 8);
		private readonly ConcurrentDictionary<string, CancellationTokenSource> thumbnailRetryDebounce = new();
		private readonly ConcurrentDictionary<ListedItem, (CancellationTokenSource Source, CancellationToken Token)> viewportPropertyCancellation = new();
		internal long PerformanceOperationId = 7;
		public uint Size { get => folderSettings.LayoutMode; set => folderSettings.LayoutMode = value; }
		public bool ShowThumbnails { get => UserSettingsService.FoldersSettingsService.ShowThumbnails; set => UserSettingsService.FoldersSettingsService.ShowThumbnails = value; }
		public int RetryCount => thumbnailRetryDebounce.Count;
		public int TestIconImageCalls => Volatile.Read(ref iconCacheService.ImageCalls);
		public DispatcherQueuePriority[] TestDispatcherPriorities => dispatcherQueue.Priorities.ToArray();
		public Func<uint, Task<BitmapImage?>>? TestIconImageHandler { set => iconCacheService.ImageHandler = value; }
		private Task<BitmapImage?> GetShieldIcon() => Task.FromResult<BitmapImage?>(new(201));
		public Task TestLoad(ListedItem item, CancellationToken token, InitialThumbnailLoadResult initial = default, bool retry = false)
			=> LoadThumbnailAsync(item, GetPropertyLoadToken(item, token), scheduleTimerRetry: retry, awaitBackgroundLoad: true, initialThumbnail: initial);
		public Task TestLoadOverlay(ListedItem item, CancellationToken token)
			=> LoadThumbnailOverlayAsync(item, folderSettings.LayoutMode, GetPropertyLoadToken(item, token), PerformanceOperationId);
		public bool TestCanReuse(ListedItem item, CancellationToken token, InitialThumbnailLoadResult initial)
			=> CanReuseInitialThumbnail(item, GetPropertyLoadToken(item, token), initial);
		public void NextGeneration() { loadPropsCTS.Cancel(); loadPropsCTS.Dispose(); loadPropsCTS = new(); }
		public void Dispose()
		{
			isDisposed = true; loadPropsCTS.Cancel();
			foreach (var entry in viewportPropertyCancellation.Values) { entry.Source.Cancel(); entry.Source.Dispose(); }
			viewportPropertyCancellation.Clear(); loadPropsCTS.Dispose();
		}
	}
}
