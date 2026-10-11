// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Media;
using System.Collections.Concurrent;
using System.IO;

namespace Files.App.Services
{
	internal sealed class IconCacheService : IIconCacheService
	{
		// Dummy path to generate generic icons for folders, executables, and shortcuts.
		private static readonly string _dummyPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "x46696c6573");

		private readonly ConcurrentDictionary<IconCacheKey, Lazy<Task<ShellImageData?>>> _data = new();

		// Images have thread affinity; only the main window's thread reads or fills this map.
		private readonly Dictionary<IconCacheKey, ImageSource> _images = new();
		private readonly Dictionary<IconCacheKey, Lazy<Task<ImageSource?>>> _imageLoads = new();
		private int _imageGeneration;

		public Task<ShellImageData?> GetIconDataAsync(string? extension, bool isFolder, uint size)
		{
			var key = GetCacheKey(extension, isFolder, size);
			if (_data.TryGetValue(key, out var cached))
				return cached.Value;

			Lazy<Task<ShellImageData?>>? entry = null;
			entry = new(() => LoadIconDataAsync(key, extension, isFolder, size, entry!));
			return _data.GetOrAdd(key, entry).Value;
		}

		private async Task<ShellImageData?> LoadIconDataAsync(IconCacheKey key, string? extension, bool isFolder, uint size, Lazy<Task<ShellImageData?>> entry)
		{
			try
			{
				// Always use the dummy path so the shell resolves the generic type icon from the
				// extension alone. This works correctly for all path types (local, MTP, FTP, network,
				// cloud, etc.) because the cache is keyed by extension anyway, not by item identity.
				// Folders use a real path so the exact-size extraction succeeds and matches per-item icons.
				var iconPath = isFolder ? Environment.SystemDirectory : GetProbePath(extension) ?? (string.IsNullOrEmpty(extension) ? _dummyPath : _dummyPath + extension);
				var data = await FileThumbnailHelper.GetImageDataAsync(iconPath, size, isFolder, IconOptions.ReturnIconOnly, priority: STATaskPriority.Fast);
				if (data is null)
					_data.TryRemove(new KeyValuePair<IconCacheKey, Lazy<Task<ShellImageData?>>>(key, entry));

				return data;
			}
			catch
			{
				// Retry failures without removing a replacement created after Clear.
				_data.TryRemove(new KeyValuePair<IconCacheKey, Lazy<Task<ShellImageData?>>>(key, entry));
				throw;
			}
		}

		public ImageSource? TryGetIconImage(string? extension, bool isFolder, uint size)
		{
			if (!IsImageThread())
				return null;

			var key = GetCacheKey(extension, isFolder, size);
			if (_images.TryGetValue(key, out var image))
				return image;

			if (_data.TryGetValue(key, out var entry) && entry.IsValueCreated && entry.Value.IsCompletedSuccessfully &&
				entry.Value.Result?.TryCreateImage() is { } created)
			{
				_images[key] = created;
				return created;
			}

			return null;
		}

		public async Task<ImageSource?> GetIconImageAsync(string? extension, bool isFolder, uint size)
		{
			if (!IsImageThread())
				return null;
			if (TryGetIconImage(extension, isFolder, size) is { } image)
				return image;

			var key = GetCacheKey(extension, isFolder, size);
			if (!_imageLoads.TryGetValue(key, out var entry))
			{
				var generation = _imageGeneration;
				entry = new(() => LoadIconImageAsync(key, extension, isFolder, size, generation));
				_imageLoads[key] = entry;
			}
			try
			{
				return await entry.Value;
			}
			finally
			{
				if (_imageLoads.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
					_imageLoads.Remove(key);
			}
		}

		private async Task<ImageSource?> LoadIconImageAsync(IconCacheKey key, string? extension, bool isFolder, uint size, int generation)
		{
			var data = await GetIconDataAsync(extension, isFolder, size);
			if (data is null || !IsImageThread())
				return null;
			if (generation == _imageGeneration && TryGetIconImage(extension, isFolder, size) is { } created)
				return created;

			var decoded = await data.CreateImageAsync(FileThumbnailHelper.GetPhysicalSize(size));
			if (decoded is not null && generation == _imageGeneration)
				_images.TryAdd(key, decoded);

			return decoded;
		}

		public async Task PreloadAsync(IEnumerable<(string? Extension, bool IsFolder)> types, uint size, CancellationToken cancellationToken = default)
		{
			var pending = new List<Task<ShellImageData?>>();
			foreach (var (extension, isFolder) in types)
			{
				var key = GetCacheKey(extension, isFolder, size);
				if (_data.TryGetValue(key, out var entry) && entry.IsValueCreated && entry.Value.IsCompleted)
					continue;
				pending.Add(GetIconDataAsync(extension, isFolder, size));
			}

			if (pending.Count > 0)
				await Task.WhenAll(pending).WaitAsync(cancellationToken);
		}

		public void Clear()
		{
			_data.Clear();
			if (IsImageThread())
			{
				_imageGeneration++;
				_images.Clear();
				_imageLoads.Clear();
			}
		}

		// An empty file of the type lets the shell scale the type icon like a real item; for paths that do not exist
		// the image-list fallback pads small icons (e.g. packaged app logos) into a corner of the jumbo cell.
		private static string? GetProbePath(string? extension)
		{
			try
			{
				var folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "IconProbes");
				var path = Path.Combine(folder, "x" + (extension ?? string.Empty).ToLowerInvariant());
				if (!File.Exists(path))
				{
					Directory.CreateDirectory(folder);
					File.WriteAllBytes(path, []);
				}

				return path;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static bool IsImageThread()
			=> MainWindow.Instance.DispatcherQueue.HasThreadAccess;

		private static IconCacheKey GetCacheKey(string? extension, bool isFolder, uint size)
			=> new(isFolder ? ":folder:" : extension?.ToLowerInvariant() ?? ":noext:", FileThumbnailHelper.GetPhysicalSize(size));

		private readonly record struct IconCacheKey(string Type, int PhysicalSize);
	}
}
