// Test doubles only: no Windows shell extraction or WinUI rendering is measured.
global using Files.App.Data.Contracts;
global using Files.App.Utils.Storage;

using System.Collections.Concurrent;
using System.Text;

namespace Microsoft.UI.Dispatching
{
	public sealed class DispatcherQueue
	{
		public static bool Enabled { get; set; } = true;
		public static DispatcherQueue? GetForCurrentThread() => Enabled ? new() : null;
	}
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
	public sealed class BitmapImage { }
}

namespace Files.App.Data.Contracts
{
	internal interface IIconCacheService
	{
		Task<byte[]?> GetIconAsync(string? itemPath, string? extension, bool isFolder, uint size);
		Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> GetIconImageAsync(string? itemPath, string? extension, bool isFolder, uint size);
		void Clear();
	}
}

namespace Files.App.Utils.Storage
{
	[Flags]
	public enum IconOptions { ReturnIconOnly = 1 }

	public static class FileThumbnailHelper
	{
		private static int _calls;
		public static int Calls => Volatile.Read(ref _calls);
		public static readonly ConcurrentQueue<(string? Path, uint Size, bool IsFolder)> Requests = new();
		public static Func<string?, uint, bool, Task<byte[]?>>? Handler { get; set; }

		public static Task<byte[]?> GetIconAsync(string? path, uint size, bool isFolder, IconOptions options)
		{
			Interlocked.Increment(ref _calls);
			Requests.Enqueue((path, size, isFolder));
			return Handler?.Invoke(path, size, isFolder) ?? Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes($"{path}:{size}:{isFolder}"));
		}

		public static void Reset()
		{
			Interlocked.Exchange(ref _calls, 0);
			Requests.Clear();
			Handler = null;
			Microsoft.UI.Dispatching.DispatcherQueue.Enabled = true;
		}
	}

	public static class BitmapExtensions
	{
		public static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> ToBitmapAsync(this byte[] data)
			=> Task.FromResult<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>(new());
	}
}
