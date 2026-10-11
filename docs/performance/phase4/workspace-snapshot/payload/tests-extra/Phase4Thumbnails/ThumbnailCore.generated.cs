using System.Buffers.Binary;
// Source methods extracted verbatim; run Prepare-ThumbnailContracts.ps1 after source changes.
#nullable enable
namespace Files.App.ViewModels { public partial class ShellViewModel {
public async Task<InitialThumbnailLoadResult> ApplyCachedThumbnailOrPreloadedIconAsync(ListedItem item, CancellationToken cancellationToken = default)
		{
			if (isDisposed)
				return default;

			var token = GetPropertyLoadToken(item, cancellationToken);
			var thumbnailSize = LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode);
			var itemPath = item.ItemPath;
			var modified = item.ItemDateModifiedReal;
			var scale = App.AppModel.AppWindowDPI;
			var showThumbnails = UserSettingsService.FoldersSettingsService.ShowThumbnails;
			var performanceOperation = PerformanceOperationId;
			var cacheQueried = false;
			BitmapImage? preloadedIconImage = null;
			try
			{
				if (!item.IsFolder
					&& FileExtensionHelpers.IsImageFile(item.FileExtension)
					&& UserSettingsService.FoldersSettingsService.ShowThumbnails)
				{
					if (thumbnailSize >= 48)
					{
						cacheQueried = true;
						var cached = await FileThumbnailHelper.GetIconAsync(
							item.ItemPath,
							thumbnailSize,
							false,
							IconOptions.ReturnThumbnailOnly | IconOptions.ReturnOnlyIfCached, token, performanceOperation);

						if (!token.IsCancellationRequested && cached is not null && item.FileImage is null)
						{
							var applied = await ApplyThumbnailImageAsync(item, cached, token, performanceOperation, onlyIfMissing: true);
							if (applied)
								return new(item, thumbnailSize, token, true, true, itemPath, modified, scale, showThumbnails);
						}
					}
				}

				if (!token.IsCancellationRequested && item.FileImage is null)
				{
					var iconSize = GetPreloadIconSize();
					var iconData = await iconCacheService.GetIconAsync(item.ItemPath, item.FileExtension, item.IsFolder, iconSize);
					token.ThrowIfCancellationRequested();
					item.PreloadedIconData = iconData;
					await dispatcherQueue.EnqueueOrInvokeAsync(async () =>
					{
						if (token.IsCancellationRequested || item.FileImage is not null)
							return;

						var iconDecodeStarted = UiPerformanceTrace.IsEnabled && performanceOperation != 0 ? Stopwatch.GetTimestamp() : 0;
						var image = await iconCacheService.GetIconImageAsync(item.ItemPath, item.FileExtension, item.IsFolder, iconSize);
						if (iconDecodeStarted != 0)
							UiPerformanceTrace.Mark(performanceOperation, "PreloadedIconImageCompleted", durationMs: Stopwatch.GetElapsedTime(iconDecodeStarted).TotalMilliseconds);
						if (!token.IsCancellationRequested && image is not null && item.FileImage is null)
						{
							item.FileImage = image;
							preloadedIconImage = image;
						}
					});
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
			}
			return new(item, thumbnailSize, token, cacheQueried, false, itemPath, modified, scale, showThumbnails, PreloadedIconImage: preloadedIconImage);
		}

public async Task<InitialThumbnailLoadResult> LoadViewportThumbnailAsync(ListedItem item, CancellationToken cancellationToken)
		{
			if (isDisposed)
				return default;
			var token = GetPropertyLoadToken(item, cancellationToken);
			token.ThrowIfCancellationRequested();
			var completed = new InitialThumbnailLoadResult(item, LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode), token, false, false,
				item.ItemPath, item.ItemDateModifiedReal, App.AppModel.AppWindowDPI, UserSettingsService.FoldersSettingsService.ShowThumbnails);
			var initial = item.FileImage is null ? await ApplyCachedThumbnailOrPreloadedIconAsync(item, token) : default;
			await LoadThumbnailAsync(item, token, awaitBackgroundLoad: true, initialThumbnail: initial, loadOverlay: false);
			token.ThrowIfCancellationRequested();
			return completed with { LoadCompleted = true };
		}

private async Task<bool> ApplyThumbnailImageAsync(ListedItem item, byte[] data, CancellationToken token, long performanceOperation,
			bool onlyIfMissing = false, Microsoft.UI.Dispatching.DispatcherQueuePriority priority = Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal)
		{
			var queued = UiPerformanceTrace.IsEnabled && performanceOperation != 0 ? Stopwatch.GetTimestamp() : 0;
			return await dispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				if (token.IsCancellationRequested || onlyIfMissing && item.FileImage is not null)
					return false;
				if (queued != 0)
					UiPerformanceTrace.Mark(performanceOperation, "ThumbnailDispatcherWait", durationMs: Stopwatch.GetElapsedTime(queued).TotalMilliseconds);
				var image = await DecodeThumbnailAsync(data, performanceOperation, LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode));
				if (token.IsCancellationRequested || image is null || onlyIfMissing && item.FileImage is not null)
					return false;
				item.FileImage = image;
				if (queued != 0)
					UiPerformanceTrace.Mark(performanceOperation, "ThumbnailApplied");
				return true;
			}, priority);
		}

private static async Task<BitmapImage?> DecodeThumbnailAsync(byte[] data, long performanceOperation, uint thumbnailSize)
		{
			var started = UiPerformanceTrace.IsEnabled && performanceOperation != 0 ? Stopwatch.GetTimestamp() : 0;
			var decodeSize = Math.Max(1, (int)Math.Ceiling(thumbnailSize * App.AppModel.AppWindowDPI));
			var image = await data.ToBitmapAsync(decodeSize, DecodePixelType.Physical, preserveAspectRatio: true, useSynchronousSmallThumbnail: true);
			if (started != 0)
				UiPerformanceTrace.Mark(performanceOperation, "ThumbnailDecodeCompleted", durationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds, value: image is null ? 0 : 1);
			return image;
		}

private async Task LoadThumbnailAsync(ListedItem item, CancellationToken cancellationToken, bool scheduleTimerRetry = true, bool awaitBackgroundLoad = false,
			InitialThumbnailLoadResult initialThumbnail = default, bool loadOverlay = true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var generationToken = loadPropsCTS.Token;
			var loadNonCachedThumbnail = false;
			var cachedThumbnailReturned = false;
			var thumbnailSize = LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode);
			var returnIconOnly = UserSettingsService.FoldersSettingsService.ShowThumbnails == false || thumbnailSize < 48;
			var performanceOperation = PerformanceOperationId;
			var reuseInitial = CanReuseInitialThumbnail(item, cancellationToken, initialThumbnail);
			var thumbnailApplied = !returnIconOnly && reuseInitial && initialThumbnail.ThumbnailApplied && item.FileImage is not null;

			byte[]? result = null;

			// Non-cached thumbnails take longer to generate
			if (item.IsFolder || !FileExtensionHelpers.IsExecutableFile(item.FileExtension))
			{
				if (!returnIconOnly)
				{
					if (!reuseInitial || !initialThumbnail.CacheQueried)
						result = await FileThumbnailHelper.GetIconAsync(
							item.ItemPath,
							thumbnailSize,
							item.IsFolder,
							IconOptions.ReturnThumbnailOnly | IconOptions.ReturnOnlyIfCached, cancellationToken, performanceOperation);

					cancellationToken.ThrowIfCancellationRequested();
					cachedThumbnailReturned = result is not null;
					loadNonCachedThumbnail = result is null && !thumbnailApplied;
				}

				// Skip the per-item icon fetch when the preloaded per-extension icon cannot differ from the final icon
				var preloadedIconIsFinal = item.PreloadedIconData is not null && !item.IsFolder &&
					(loadNonCachedThumbnail && FileExtensionHelpers.IsImageFile(item.FileExtension) ||
					returnIconOnly && !item.IsShortcut && !HasPerFileIcon(item.FileExtension));

				if (result is null && !preloadedIconIsFinal && !thumbnailApplied)
				{
					// Get icon
					result = await FileThumbnailHelper.GetIconAsync(
							item.ItemPath,
							thumbnailSize,
							item.IsFolder,
							IconOptions.ReturnIconOnly, cancellationToken, performanceOperation);

					cancellationToken.ThrowIfCancellationRequested();
				}
				else if (result is null && !thumbnailApplied &&
					!(reuseInitial && initialThumbnail.PreloadedIconImage is not null && ReferenceEquals(item.FileImage, initialThumbnail.PreloadedIconImage)))
				{
					// The final icon is the shared per-extension icon; apply it so an image from a previous layout size doesn't linger
					await dispatcherQueue.EnqueueOrInvokeAsync(async () =>
					{
						if (cancellationToken.IsCancellationRequested)
							return;
						var image = await iconCacheService.GetIconImageAsync(item.ItemPath, item.FileExtension, item.IsFolder, thumbnailSize);
						if (!cancellationToken.IsCancellationRequested && image is not null)
							item.FileImage = image;
					}, Microsoft.UI.Dispatching.DispatcherQueuePriority.Low);

					cancellationToken.ThrowIfCancellationRequested();
				}
			}
			else
			{
				// Get icon or thumbnail
				result = await FileThumbnailHelper.GetIconAsync(
						item.ItemPath,
						thumbnailSize,
						item.IsFolder,
						(returnIconOnly ? IconOptions.ReturnIconOnly : IconOptions.None), cancellationToken, performanceOperation);

				cancellationToken.ThrowIfCancellationRequested();
			}

			if (result is not null)
			{
				var imageApplied = await ApplyThumbnailImageAsync(item, result, cancellationToken, performanceOperation);
				if (!imageApplied && cachedThumbnailReturned)
					loadNonCachedThumbnail = true;
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (loadNonCachedThumbnail)
			{
				// Get non-cached thumbnail asynchronously
				var backgroundLoad = Task.Run(async () =>
				{
					try
					{
						await loadThumbnailSemaphore.WaitAsync(cancellationToken);
						try
						{
							result = await FileThumbnailHelper.GetIconAsync(
									item.ItemPath,
									thumbnailSize,
									item.IsFolder,
									IconOptions.ReturnThumbnailOnly, cancellationToken, performanceOperation);
						}
						finally
						{
							loadThumbnailSemaphore.Release();
						}

						cancellationToken.ThrowIfCancellationRequested();

						if (result is null)
						{
							item.NeedsDelayedThumbnailLoad = true;

							// Some writers never emit a FILE_ACTION_MODIFIED event after finalizing the file, so the normal event-driven retry never fires.
							// Schedule a 2s timer as a fallback; the FILE_ACTION_MODIFIED handler cancels it if the event arrives first.
							if (scheduleTimerRetry)
							{
								var retryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, generationToken);
								// Capture the token before publishing the source to the debounce map,
								// as another thread may remove and dispose it at any point afterwards
								var retryToken = retryCts.Token;
								if (thumbnailRetryDebounce.TryAdd(item.GetRequiredPath(), retryCts))
								{
									App.Logger.LogWarning("Thumbnail load failed [{Id}] '{Extension}'; scheduling 2s timer retry.", item.GetRequiredPath().GetHashCode(), Path.GetExtension(item.ItemPath));

									_ = RetryThumbnailAfterDelayAsync(item, retryCts, retryToken, 2000, scheduleTimerRetry: false);
								}
								else
								{
									App.Logger.LogWarning("Thumbnail load failed [{Id}] '{Extension}'; mod-retry already pending, skipping timer.", item.GetRequiredPath().GetHashCode(), Path.GetExtension(item.ItemPath));
									retryCts.Dispose();
								}
							}
							else
							{
								App.Logger.LogWarning("Thumbnail load failed [{Id}] '{Extension}' on timer retry; awaiting next FILE_ACTION_MODIFIED.", item.GetRequiredPath().GetHashCode(), Path.GetExtension(item.ItemPath));
							}
						}
						else
						{
							await ApplyThumbnailImageAsync(item, result, cancellationToken, performanceOperation,
								priority: Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal);
						}
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
					}
				}, cancellationToken);
				if (awaitBackgroundLoad)
					await backgroundLoad;
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (loadOverlay)
				await LoadThumbnailOverlayAsync(item, thumbnailSize, cancellationToken, performanceOperation);
		}

private async Task LoadThumbnailOverlayAsync(ListedItem item, uint thumbnailSize, CancellationToken cancellationToken, long performanceOperation)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var iconOverlay = await FileThumbnailHelper.GetIconOverlayAsync(item.ItemPath, thumbnailSize, item.IsFolder, cancellationToken, performanceOperation);
			cancellationToken.ThrowIfCancellationRequested();

			if (iconOverlay is not null)
			{
				await dispatcherQueue.EnqueueOrInvokeAsync(async () =>
				{
					if (cancellationToken.IsCancellationRequested)
						return;
					if (iconOverlay is not null)
					{
						var overlayImage = await iconOverlay.ToBitmapAsync();
						var shieldIcon = await GetShieldIcon();
						if (!cancellationToken.IsCancellationRequested)
						{
							item.IconOverlay = overlayImage;
							item.ShieldIcon = shieldIcon;
						}
					}
				}, Microsoft.UI.Dispatching.DispatcherQueuePriority.Low);

				cancellationToken.ThrowIfCancellationRequested();
			}
		}

private async Task RetryThumbnailAfterDelayAsync(ListedItem item, CancellationTokenSource source, CancellationToken token, int delayMs, bool scheduleTimerRetry = true)
		{
			var entry = new KeyValuePair<string, CancellationTokenSource>(item.GetRequiredPath(), source);
			var ownsSource = false;
			try
			{
				await Task.Delay(delayMs, token);
				ownsSource = thumbnailRetryDebounce.TryRemove(entry);
				if (!ownsSource)
					return;

				token.ThrowIfCancellationRequested();
				item.NeedsDelayedThumbnailLoad = false;
				await LoadThumbnailAsync(item, token, scheduleTimerRetry, awaitBackgroundLoad: true);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
			}
			finally
			{
				// A watcher or navigation that removed the entry already owns its cleanup.
				if (ownsSource || thumbnailRetryDebounce.TryRemove(entry))
					source.Dispose();
			}
		}

private CancellationToken GetPropertyLoadToken(ListedItem item, CancellationToken cancellationToken)
		{
			if (!cancellationToken.CanBeCanceled || cancellationToken.IsCancellationRequested)
				return cancellationToken.IsCancellationRequested ? cancellationToken : loadPropsCTS.Token;

			while (viewportPropertyCancellation.TryGetValue(item, out var existing))
			{
				if (!existing.Token.IsCancellationRequested)
					return existing.Token;
				if (viewportPropertyCancellation.TryRemove(new(item, existing)))
					existing.Source.Dispose();
			}

			var candidate = CancellationTokenSource.CreateLinkedTokenSource(loadPropsCTS.Token, cancellationToken);
			var selected = viewportPropertyCancellation.GetOrAdd(item, (candidate, candidate.Token));
			if (!ReferenceEquals(candidate, selected.Source))
				candidate.Dispose();
			return selected.Token;
		}

public readonly record struct InitialThumbnailLoadResult(ListedItem? Item, uint Size, CancellationToken Token, bool CacheQueried, bool ThumbnailApplied,
			string? Path, DateTimeOffset Modified, float Scale, bool ShowThumbnails, bool LoadCompleted = false, BitmapImage? PreloadedIconImage = null);

private bool CanReuseInitialThumbnail(ListedItem item, CancellationToken token, InitialThumbnailLoadResult initial)
			=> ReferenceEquals(initial.Item, item) && initial.Size == LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode) && initial.Token == token &&
				initial.Path == item.ItemPath && initial.Modified == item.ItemDateModifiedReal && initial.Scale == App.AppModel.AppWindowDPI &&
				initial.ShowThumbnails == UserSettingsService.FoldersSettingsService.ShowThumbnails;

public bool IsViewportThumbnailCurrent(ListedItem item, InitialThumbnailLoadResult initial)
			=> !initial.Token.IsCancellationRequested && initial.LoadCompleted && CanReuseInitialThumbnail(item, initial.Token, initial);

private uint GetPreloadIconSize()
			=> LayoutSizeKindHelper.GetIconSize(folderSettings.LayoutMode);

private static bool HasPerFileIcon(string? extension)
			=> extension is not null && _perFileIconExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

private static readonly string[] _perFileIconExtensions = [".ico", ".cur", ".ani", ".scr", ".msc", ".appref-ms"];
}}
namespace Files.App.Helpers { public static partial class BitmapHelper {
private static void SetDecodeSize(BitmapImage image, byte[] data, int decodeSize, bool preserveAspectRatio)
		{
			if (decodeSize <= 0)
				return;

			if (!preserveAspectRatio)
			{
				image.DecodePixelWidth = decodeSize;
				image.DecodePixelHeight = decodeSize;
				return;
			}

			// Shell bitmaps are encoded as PNG; constrain only their longer side to preserve proportions.
			if (TryGetPngDimensions(data, out var width, out var height) && height > width)
			{
				image.DecodePixelHeight = decodeSize;
				return;
			}

			image.DecodePixelWidth = decodeSize;
		}

private static bool ShouldUseSynchronousSmallThumbnail(byte[] data, int decodeSize, DecodePixelType decodePixelType,
			bool preserveAspectRatio, bool useSynchronousSmallThumbnail)
		{
			// Avoid a dispatcher handoff only for bounded Shell thumbnails; larger images stay asynchronous.
			return useSynchronousSmallThumbnail && preserveAspectRatio && decodePixelType == DecodePixelType.Physical &&
				decodeSize > 0 && decodeSize <= 256 && data.Length <= 256 * 1024 &&
				TryGetPngDimensions(data, out var width, out var height) && width <= 256 && height <= 256;
		}

private static bool TryGetPngDimensions(byte[] data, out uint width, out uint height)
		{
			width = height = 0;
			ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
			if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(pngSignature) ||
				BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)) != 13 || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
			{
				return false;
			}

			width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
			height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
			return width > 0 && width <= int.MaxValue && height > 0 && height <= int.MaxValue;
		}
}}
