// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.ViewModels
{
	/// <summary>
	/// Row images in tiers: a remembered or generic image in the realization frame (tier 0), cached thumbnails and
	/// per-item icons on the fast STA lane (tier 1), and thumbnail generation on the slow lane (tier 2).
	/// </summary>
	public sealed partial class ShellViewModel
	{
		// Placeholders, pinned/unpinned and recall-on-access files may get thumbnails from their sync provider
		private const FileAttributes CloudAttributes = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x80000 | (FileAttributes)0x100000 | (FileAttributes)0x400000;

		// Partial downloads and temporary files are still being written; a missing thumbnail is retried for them
		private static readonly string[] _inProgressExtensions = [".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload"];

		private readonly ThumbnailImageCache imageCache = ThumbnailImageCache.Default;
		private readonly ConcurrentQueue<PendingImage> pendingImages = new();
		private int pendingImagesScheduled;

		// Overlays repeat across rows (sync states, links); identical bytes share one image
		private readonly Dictionary<(int Length, ulong Hash), BitmapImage> overlayImages = new();

		[Flags]
		internal enum ItemImageWork
		{
			None = 0,
			GenericIcon = 1,
			CachedThumbnail = 2,
			ItemIcon = 4,
		}

		internal readonly record struct ItemImageRequest(ListedItem Item, int Token, string Path, DateTimeOffset Modified, uint Size, int PhysicalSize,
			bool Thumbnails, bool IsFolder, bool ThumbnailExpected, bool IconExpected, long FileSizeBytes, bool CloudPlaceholder);

		private sealed record PendingImage(ItemImageRequest Request, ShellImageData? Data, ThumbnailImageKind? Kind, bool Final, CancellationToken Token, bool Missing = false);

		private uint GetPreloadIconSize()
			=> LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode);

		private ItemImageRequest CreateImageRequest(ListedItem item, bool ignoreMissing = false)
		{
			var size = LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode);
			var thumbnails = UserSettingsService.FoldersSettingsService.ShowThumbnails && size >= 48;
			var path = item.ItemPath ?? string.Empty;
			var thumbnailExpected = thumbnails && path.Length > 0 && CanHaveThumbnail(item) &&
				(ignoreMissing || !imageCache.IsMissing(path, item.ItemDateModifiedReal));
			var iconExpected = path.Length > 0 && HasPerItemIcon(item);
			return new(item, item.ImageToken, path, item.ItemDateModifiedReal, size, FileThumbnailHelper.GetPhysicalSize(size),
				thumbnails, item.IsFolder && !item.IsArchive, thumbnailExpected, iconExpected, item.FileSizeBytes, IsCloudPlaceholder(item));
		}

		private static bool IsCloudPlaceholder(ListedItem item)
		{
			if (item.SyncStatusUI.SyncStatus == CloudDriveSyncStatus.FileOnline)
				return true;
			if (item.SyncStatusUI.SyncStatus is CloudDriveSyncStatus.FileOffline or CloudDriveSyncStatus.FileOfflinePinned)
				return false;
			const FileAttributes recall = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;
			return item.Win32Attributes is { } attributes && (attributes & recall) != 0;
		}

		private static bool CanHaveThumbnail(ListedItem item)
		{
			if (item is AlternateStreamItem or FtpItem || item.IsLibrary || FtpHelpers.IsFtpPath(item.ItemPath))
				return false;
			if (item.IsFolder && !item.IsArchive)
				return true;
			if (item.Win32Attributes is { } attributes && (attributes & CloudAttributes) != 0)
				return true;
			if (item.ItemPath?.StartsWith(@"\\?\", StringComparison.Ordinal) is true)
				return true;

			return ShellAssociationCache.HasThumbnailProvider(item.FileExtension);
		}

		private static bool HasPerItemIcon(ListedItem item)
		{
			if (item is AlternateStreamItem or FtpItem)
				return false;
			if (item.IsLibrary || item.IsShortcut)
				return true;

			// The shell only reads desktop.ini for folders marked ReadOnly or System
			if (item.IsFolder && !item.IsArchive)
				return item.Win32Attributes is not { } attributes || (attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0;

			return ShellAssociationCache.HasPerFileIcon(item.FileExtension);
		}

		private static bool IsStillBeingWritten(ListedItem item, DateTimeOffset modified)
			=> _inProgressExtensions.Contains(item.FileExtension, StringComparer.OrdinalIgnoreCase) ||
				!item.IsFolder && item.FileSizeBytes == 0 ||
				DateTimeOffset.Now - modified < TimeSpan.FromMinutes(2);

		/// <summary>
		/// Starts a new image generation for a row that was just realized; results for the previous one are ignored.
		/// </summary>
		internal void BeginItemImage(ListedItem item)
			=> item.ImageToken++;

		/// <summary>
		/// Tier 0: paints the row in the realization frame with its remembered image or the generic icon of its type.
		/// </summary>
		/// <returns>The work left for tier 1.</returns>
		internal (ItemImageWork Work, ItemImageRequest Request) ApplyImmediateImage(ListedItem item)
		{
			if (isDisposed || string.IsNullOrEmpty(item.ItemPath))
				return default;

			var request = CreateImageRequest(item);
			if (request.ThumbnailExpected && imageCache.TryGet(GetImageKey(request, ThumbnailImageKind.Thumbnail), out var thumbnail))
			{
				SetItemImage(item, thumbnail, GetImageKey(request, ThumbnailImageKind.Thumbnail), true);
				return (ItemImageWork.None, request);
			}

			var work = request.ThumbnailExpected ? ItemImageWork.CachedThumbnail : ItemImageWork.None;
			if (request.IconExpected)
			{
				if (imageCache.TryGet(GetImageKey(request, ThumbnailImageKind.Icon), out var icon))
				{
					SetItemImage(item, icon, GetImageKey(request, ThumbnailImageKind.Icon), !request.ThumbnailExpected);
					return (work, request);
				}

				work |= ItemImageWork.ItemIcon;
			}

			// An image of this same file (e.g. before a layout size change) stays until the better one arrives
			if (item.FileImage is not null && work != ItemImageWork.None &&
				(request.Thumbnails || item.ImageKey.Kind != ThumbnailImageKind.Thumbnail) &&
				(item.ImageKey.Path is null || IsSameFile(item.ImageKey, request)))
				return (work, request);

			if (iconCacheService.TryGetIconImage(item.FileExtension, request.IsFolder, request.Size) is { } generic)
			{
				SetItemImage(item, generic, default, work == ItemImageWork.None);
				return (work, request);
			}

			// The exact generic icon is still loading; a shared fallback avoids an empty tile meanwhile
			if (item.FileImage is null && iconCacheService.TryGetIconImage(null, request.IsFolder, request.Size) is { } fallback)
				SetItemImage(item, fallback, default, false);

			return (work | ItemImageWork.GenericIcon, request);
		}

		/// <summary>
		/// Tier 1: generic icon if missing, cached thumbnail, then the per-item icon, all on the fast STA lane.
		/// </summary>
		/// <returns>True when the thumbnail still has to be generated (tier 2).</returns>
		internal async Task<bool> LoadFastImageAsync(ItemImageRequest request, ItemImageWork work, CancellationToken token)
		{
			try
			{
				if (work.HasFlag(ItemImageWork.GenericIcon) &&
					await iconCacheService.GetIconDataAsync(request.Item.FileExtension, request.IsFolder, request.Size).ConfigureAwait(false) is { } generic)
					PostImage(new(request, generic, null, false, token));

				if (work.HasFlag(ItemImageWork.CachedThumbnail))
				{
					var cached = await FileThumbnailHelper.GetImageDataAsync(request.Path, request.Size, request.IsFolder,
						IconOptions.ReturnThumbnailOnly | IconOptions.ReturnOnlyIfCached, token, STATaskPriority.Fast).ConfigureAwait(false);
					if (cached is not null)
					{
						PostImage(new(request, cached, ThumbnailImageKind.Thumbnail, true, token));
						return false;
					}
				}

				if (work.HasFlag(ItemImageWork.ItemIcon))
				{
					var options = IconOptions.ReturnIconOnly | (request.CloudPlaceholder ? IconOptions.ReturnOnlyIfCached : IconOptions.None);
					var icon = await FileThumbnailHelper.GetImageDataAsync(request.Path, request.Size, request.IsFolder,
						options, token, STATaskPriority.Fast).ConfigureAwait(false);
					if (icon is not null)
						PostImage(new(request, icon, ThumbnailImageKind.Icon, !request.ThumbnailExpected, token));
				}

				if (!request.ThumbnailExpected || request.CloudPlaceholder)
					PostImage(new(request, null, null, true, token));

				return request.ThumbnailExpected && !request.CloudPlaceholder;
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		/// <summary>
		/// Tier 2: generates the thumbnail on the slow STA lane.
		/// </summary>
		internal async Task LoadSlowImageAsync(ItemImageRequest request, CancellationToken token, bool scheduleTimerRetry = true)
		{
			try
			{
				if (request.CloudPlaceholder)
				{
					PostImage(new(request, null, null, true, token));
					return;
				}
				var data = await FileThumbnailHelper.GetImageDataAsync(request.Path, request.Size, request.IsFolder,
					IconOptions.ReturnThumbnailOnly, token, STATaskPriority.Slow).ConfigureAwait(false);
				if (data is not null)
				{
					PostImage(new(request, data, ThumbnailImageKind.Thumbnail, true, token));
					return;
				}

				var retry = IsStillBeingWritten(request.Item, request.Modified);
				PostImage(new(request, null, null, true, token, Missing: !retry));
				if (!retry)
					return;

				// Some writers never emit a FILE_ACTION_MODIFIED event after finalizing the file, so the normal event-driven retry never fires.
				// Schedule a 2s timer as a fallback; the FILE_ACTION_MODIFIED handler cancels it if the event arrives first.
				await dispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					if (!IsCurrent(request, token))
						return false;
					var item = request.Item;
					item.NeedsDelayedThumbnailLoad = true;
					if (!scheduleTimerRetry)
						return true;
					var retryCts = CancellationTokenSource.CreateLinkedTokenSource(loadPropsCTS.Token);
					// Capture the token before publishing the source to the debounce map,
					// as another thread may remove and dispose it at any point afterwards
					var retryToken = retryCts.Token;
					if (thumbnailRetryDebounce.TryAdd(request.Path, retryCts))
						_ = RetryThumbnailAfterDelayAsync(item, retryCts, retryToken, 2000, scheduleTimerRetry: false);
					else
						retryCts.Dispose();
					return true;
				}, DispatcherQueuePriority.Low);
			}
			catch (OperationCanceledException)
			{
			}
		}

		/// <summary>
		/// Loads a realized row's image again after its file changed, keeping the current image until the new one is ready.
		/// </summary>
		internal async Task ReloadItemImageAsync(ListedItem item, CancellationToken token, bool scheduleTimerRetry = true)
		{
			var request = await dispatcherQueue.EnqueueOrInvokeAsync(() =>
				item.FileImage is null || isDisposed || token.IsCancellationRequested ? (ItemImageRequest?)null : CreateImageRequest(item, ignoreMissing: true));
			if (request is not { } current)
				return;

			var work = (current.ThumbnailExpected ? ItemImageWork.CachedThumbnail : ItemImageWork.None) | (current.IconExpected ? ItemImageWork.ItemIcon : ItemImageWork.None);
			if (await LoadFastImageAsync(current, work, token))
				await LoadSlowImageAsync(current, token, scheduleTimerRetry);
		}

		/// <summary>
		/// Re-runs every tier for a visible row whose type changed (e.g. renamed to another extension).
		/// </summary>
		internal void RefreshVisibleItemImage(ListedItem item)
		{
			BeginItemImage(item);
			var (work, request) = ApplyImmediateImage(item);
			if (work != ItemImageWork.None)
				_ = RunItemImageWorkAsync(request, work, loadPropsCTS.Token);
		}

		private async Task RunItemImageWorkAsync(ItemImageRequest request, ItemImageWork work, CancellationToken token)
		{
			if (await LoadFastImageAsync(request, work, token))
				await LoadSlowImageAsync(request, token);
		}
		/// <summary>
		/// Forgets remembered images of an item and loads them again (e.g. after rotating an image or changing a folder icon).
		/// </summary>
		public Task RefreshItemImageAsync(ListedItem item)
		{
			if (item.ItemPath is not { } path)
				return Task.CompletedTask;

			imageCache.Remove(path);
			BeginItemImage(item);
			item.ImageKey = default;
			item.HasFinalImage = false;
			return ReloadItemImageAsync(item, loadPropsCTS.Token);
		}

		/// <summary>
		/// Drops a recycled row's image reference; the image itself stays in the shared cache.
		/// </summary>
		public void ReleaseItemImage(ListedItem item)
		{
			if (item.ItemPath is { } path && thumbnailRetryDebounce.TryRemove(path, out var retry))
			{
				retry.Cancel();
				retry.Dispose();
			}
			item.ImageToken++;
			item.FileImage = null;
			item.ImageKey = default;
			item.HasFinalImage = false;
		}

		private static ThumbnailImageKey GetImageKey(in ItemImageRequest request, ThumbnailImageKind kind)
			=> ThumbnailImageKey.Create(request.Path, request.Modified, request.PhysicalSize, kind, request.FileSizeBytes);

		private static bool IsSameFile(in ThumbnailImageKey key, in ItemImageRequest request)
			=> key.Path == request.Path && key.ModifiedTicks == request.Modified.UtcTicks && key.FileSizeBytes == request.FileSizeBytes;

		private static void SetItemImage(ListedItem item, ImageSource image, ThumbnailImageKey key, bool final)
		{
			item.FileImage = image;
			item.ImageKey = key;
			item.HasFinalImage = final;
		}

		// The request still describes the row: same realization, same file version, same layout size and mode.
		private bool IsCurrent(ItemImageRequest request, CancellationToken token)
		{
			var item = request.Item;
			return !isDisposed && !token.IsCancellationRequested && item.ImageToken == request.Token &&
				string.Equals(item.ItemPath, request.Path, StringComparison.Ordinal) && item.ItemDateModifiedReal == request.Modified &&
				item.FileSizeBytes == request.FileSizeBytes &&
				LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode) == request.Size &&
				FileThumbnailHelper.GetPhysicalSize(request.Size) == request.PhysicalSize &&
				(UserSettingsService.FoldersSettingsService.ShowThumbnails && request.Size >= 48) == request.Thumbnails;
		}

		private void PostImage(PendingImage pending)
		{
			pendingImages.Enqueue(pending);
			if (Interlocked.Exchange(ref pendingImagesScheduled, 1) == 0 && !dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, ApplyPendingImages))
				Volatile.Write(ref pendingImagesScheduled, 0);
		}

		// One dispatcher callback applies a batch of finished images within a small frame budget.
		private void ApplyPendingImages()
		{
			var started = Stopwatch.GetTimestamp();
			while (pendingImages.TryDequeue(out var pending))
			{
				ApplyPendingImage(pending);
				if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 4)
					break;
			}

			Volatile.Write(ref pendingImagesScheduled, 0);
			if (!pendingImages.IsEmpty && Interlocked.Exchange(ref pendingImagesScheduled, 1) == 0 && !dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, ApplyPendingImages))
				Volatile.Write(ref pendingImagesScheduled, 0);
		}

		private void ApplyPendingImage(PendingImage pending)
		{
			if (isDisposed)
				return;

			var request = pending.Request;
			var item = request.Item;
			var current = IsCurrent(request, pending.Token);
			if (!current)
				return;
			if (pending.Data is null)
			{
				if (pending.Missing)
					imageCache.MarkMissing(request.Path, request.Modified);
				if (current && pending.Final && item.FileImage is not null)
					item.HasFinalImage = true;
				return;
			}

			if (pending.Kind is not { } kind)
			{
				// Generic icon finished loading: replace a fallback, never a better image
				if (current && item.ImageKey.Path is null && !item.HasFinalImage &&
					iconCacheService.TryGetIconImage(item.FileExtension, request.IsFolder, request.Size) is { } generic)
					SetItemImage(item, generic, default, !request.ThumbnailExpected && !request.IconExpected);
				return;
			}

			var key = GetImageKey(request, kind);
			var image = pending.Data.TryCreateImage();
			if (image is null)
			{
				_ = ApplyEncodedImageAsync(pending, key);
				return;
			}

			imageCache.Add(key, image, pending.Data.ByteCount);
			if (current && !IsThumbnailOf(item.ImageKey, request))
				SetItemImage(item, image, key, pending.Final);
		}

		private async Task ApplyEncodedImageAsync(PendingImage pending, ThumbnailImageKey key)
		{
			var image = await pending.Data!.CreateImageAsync(pending.Request.PhysicalSize);
			if (image is null || !IsCurrent(pending.Request, pending.Token))
				return;

			imageCache.Add(key, image, pending.Data.GetDecodedByteCount(image, pending.Request.PhysicalSize));
			if (IsCurrent(pending.Request, pending.Token) && !IsThumbnailOf(pending.Request.Item.ImageKey, pending.Request))
				SetItemImage(pending.Request.Item, image, key, pending.Final);
		}

		// A row showing its thumbnail never goes back to an icon
		private static bool IsThumbnailOf(in ThumbnailImageKey key, in ItemImageRequest request)
			=> request.Thumbnails && key.Kind == ThumbnailImageKind.Thumbnail && key.Path is not null && IsSameFile(key, request) && key.PhysicalSize == request.PhysicalSize;

		/// <summary>
		/// Loads the generic icons of every type in a listing in parallel, so rows paint them in their first frame.
		/// </summary>
		private async Task PreloadGenericIconsAsync(IEnumerable<ListedItem> items, CancellationToken cancellationToken)
		{
			var size = LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode);
			var types = new HashSet<(string?, bool)>
			{
				// Shared fallbacks for types discovered later
				(null, false),
				(null, true),
			};
			foreach (var item in items)
			{
				var isFolder = item.IsFolder && !item.IsArchive;
				types.Add((isFolder ? null : item.FileExtension?.ToLowerInvariant(), isFolder));
			}

			try
			{
				// A stuck shell extension must not hold the listing back
				await iconCacheService.PreloadAsync(types, size, cancellationToken).WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
			}
			catch (TimeoutException)
			{
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				App.Logger.LogWarning(ex, "Failed to preload generic icons.");
			}
		}

		private BitmapImage? GetSharedOverlay(byte[] data, BitmapImage created)
		{
			var key = (data.Length, HashOverlay(data));
			if (overlayImages.TryGetValue(key, out var existing))
				return existing;
			if (overlayImages.Count >= 256)
				overlayImages.Clear();
			overlayImages[key] = created;
			return created;
		}

		private static ulong HashOverlay(ReadOnlySpan<byte> data)
		{
			var hash = 14695981039346656037UL;
			foreach (var value in data)
				hash = (hash ^ value) * 1099511628211UL;
			return hash;
		}
	}
}
