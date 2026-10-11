// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Concurrent;
using System.IO;

namespace Files.App.Services
{
	internal sealed class IconCacheService : IIconCacheService
	{
		// Dummy path to generate generic icons for folders, executables, and shortcuts.
		private static readonly string _dummyPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "x46696c6573");

		private readonly ConcurrentDictionary<IconCacheKey, Lazy<Task<byte[]?>>> _cache = new();
		private ConcurrentDictionary<IconCacheKey, Lazy<Task<BitmapImage?>>> _imageCache = new();

		public Task<byte[]?> GetIconAsync(string? itemPath, string? extension, bool isFolder, uint size)
		{
			var key = GetCacheKey(extension, isFolder, size);

			if (_cache.TryGetValue(key, out var cached))
				return cached.Value;

			return GetOrCreateIconAsync(key, extension, isFolder, size);
		}

		private Task<byte[]?> GetOrCreateIconAsync(IconCacheKey key, string? extension, bool isFolder, uint size)
		{
			Lazy<Task<byte[]?>>? entry = null;
			entry = new(() => LoadIconAsync(key, extension, isFolder, size, entry!));
			return _cache.GetOrAdd(key, entry).Value;
		}

		private async Task<byte[]?> LoadIconAsync(IconCacheKey key, string? extension, bool isFolder, uint size, Lazy<Task<byte[]?>> entry)
		{
			try
			{
				// Always use the dummy path so the shell resolves the generic type icon from the
				// extension alone. This works correctly for all path types (local, MTP, FTP, network,
				// cloud, etc.) because the cache is keyed by extension anyway, not by item identity.
				// Folders use a real path so the exact-size extraction succeeds and matches per-item icons.
				var iconPath = isFolder ? Environment.SystemDirectory : (string.IsNullOrEmpty(extension) ? _dummyPath : _dummyPath + extension);

				return await FileThumbnailHelper.GetIconAsync(
					iconPath,
					size,
					isFolder,
					IconOptions.ReturnIconOnly);
			}
			catch
			{
				// Retry failures without removing a replacement created after Clear.
				_cache.TryRemove(new KeyValuePair<IconCacheKey, Lazy<Task<byte[]?>>>(key, entry));
				throw;
			}
		}

		public Task<BitmapImage?> GetIconImageAsync(string? itemPath, string? extension, bool isFolder, uint size)
		{
			// BitmapImage has thread affinity, so only the main window's dispatcher may use this cache
			if (DispatcherQueue.GetForCurrentThread() is null)
				return Task.FromResult<BitmapImage?>(null);

			var key = GetCacheKey(extension, isFolder, size);
			var imageCache = Volatile.Read(ref _imageCache);
			if (imageCache.TryGetValue(key, out var cached))
				return cached.Value;

			Lazy<Task<BitmapImage?>>? entry = null;
			entry = new(() => LoadImageAsync(imageCache, key, itemPath, extension, isFolder, size, entry!));
			return imageCache.GetOrAdd(key, entry).Value;
		}

		private async Task<BitmapImage?> LoadImageAsync(ConcurrentDictionary<IconCacheKey, Lazy<Task<BitmapImage?>>> imageCache,
			IconCacheKey key, string? itemPath, string? extension, bool isFolder, uint size, Lazy<Task<BitmapImage?>> entry)
		{
			try
			{
				var data = await GetIconAsync(itemPath, extension, isFolder, size);
				var decodeSize = Math.Max(1, (int)Math.Ceiling(size * App.AppModel.AppWindowDPI));
				var image = data is null ? null : await data.ToBitmapAsync(decodeSize, DecodePixelType.Physical,
					preserveAspectRatio: true, useSynchronousSmallThumbnail: true);
				if (image is null)
					imageCache.TryRemove(new KeyValuePair<IconCacheKey, Lazy<Task<BitmapImage?>>>(key, entry));

				return image;
			}
			catch
			{
				imageCache.TryRemove(new KeyValuePair<IconCacheKey, Lazy<Task<BitmapImage?>>>(key, entry));
				throw;
			}
		}

		public void Clear()
		{
			_cache.Clear();
			// Pending decodes may complete, but cannot repopulate the current generation.
			Interlocked.Exchange(ref _imageCache, new());
		}

		private static IconCacheKey GetCacheKey(string? extension, bool isFolder, uint size)
			=> new(isFolder ? ":folder:" : extension?.ToLowerInvariant() ?? ":noext:", size);

		private readonly record struct IconCacheKey(string Type, uint Size);
	}
}
