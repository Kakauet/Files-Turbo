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
		private readonly ConcurrentDictionary<IconCacheKey, BitmapImage> _imageCache = new();

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

		public async Task<BitmapImage?> GetIconImageAsync(string? itemPath, string? extension, bool isFolder, uint size)
		{
			// BitmapImage has thread affinity, so only the main window's dispatcher may use this cache
			if (DispatcherQueue.GetForCurrentThread() is null)
				return null;

			var key = GetCacheKey(extension, isFolder, size);
			if (_imageCache.TryGetValue(key, out var cached))
				return cached;

			var data = await GetIconAsync(itemPath, extension, isFolder, size);
			if (data is null)
				return null;

			var image = await data.ToBitmapAsync();
			if (image is not null)
				_imageCache.TryAdd(key, image);

			return image;
		}

		public void Clear()
		{
			_cache.Clear();
			_imageCache.Clear();
		}

		private static IconCacheKey GetCacheKey(string? extension, bool isFolder, uint size)
			=> new(isFolder ? ":folder:" : extension?.ToLowerInvariant() ?? ":noext:", size);

		private readonly record struct IconCacheKey(string Type, uint Size);
	}
}
