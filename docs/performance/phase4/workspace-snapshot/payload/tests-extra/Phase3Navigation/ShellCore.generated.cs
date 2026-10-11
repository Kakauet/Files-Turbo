// Generated from the actual ShellViewModel methods. Do not edit.
namespace Files.App.ViewModels { public partial class ShellViewModel {
public void CancelLoadAndClearFiles()
		{
			Debug.WriteLine("CancelLoadAndClearFiles");
			CloseWatcher();
			CancelNetworkAvailabilityUpdate();
			IsNetworkDiscoveryInfoBarOpen = false;
			// Pending dispatcher work and queued loads also belong to the old location.
			bool requestTrim;
			lock (loadCollectionSync)
			{
				requestTrim = filesAndFolders.Count >= 10_000;
				addFilesCTS.Cancel();
				addFilesCTS = new CancellationTokenSource();
				filesAndFolders.Clear();
			}
			CancelExtendedPropertiesLoading();
			foreach (var key in thumbnailRetryDebounce.Keys)
			{
				// Only the thread that removes an entry may cancel/dispose it, otherwise
				// a concurrent retry continuation could touch an already disposed source
				if (thumbnailRetryDebounce.TryRemove(key, out var cts))
				{
					cts.Cancel();
					cts.Dispose();
				}
			}
			if (requestTrim)
				AppMemoryHelper.RequestTrim();
			FilesAndFolders.Clear();
			CancelSearch();
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

private bool CompleteItemPropertyLoad(ListedItem item, CancellationToken token, bool canceled)
		{
			if (!itemLoadQueue.TryGetValue(item, out var load) || load.Token != token ||
				!itemLoadQueue.TryRemove(new(item, load)))
				return false;
			if (canceled)
				item.ItemPropertiesInitialized = false;
			return true;
		}

public void CancelExtendedPropertiesLoadingForItem(ListedItem item)
		{
			if (itemLoadQueue.TryGetValue(item, out var load))
				itemLoadQueue.TryUpdate(item, (load.Token, true), load);
			if (viewportPropertyCancellation.TryRemove(item, out var source))
			{
				source.Source.Cancel();
				source.Source.Dispose();
			}
		}

public void CancelExtendedPropertiesLoading()
		{
			if (isDisposed)
				return;

			loadPropsCTS.Cancel();
			loadPropsCTS = new CancellationTokenSource();
			foreach (var entry in viewportPropertyCancellation)
			{
				if (viewportPropertyCancellation.TryRemove(entry))
				{
					entry.Value.Source.Cancel();
					entry.Value.Source.Dispose();
				}
			}

			// Release parked loads so they can observe the cancellation
			Interlocked.Exchange(ref scrollSettledTcs, null)?.TrySetResult();
		}

public async Task ApplyFilesAndFoldersChangesAsync(CancellationToken cancellationToken = default)
		{
			using var applyCTS = CancellationTokenSource.CreateLinkedTokenSource(addFilesCTS.Token, cancellationToken);
			var token = applyCTS.Token;
			var performanceOperation = PerformanceOperationId;
			try
			{
				// Preserve realized rows for unchanged snapshots and ungrouped tail additions.
				// Replacements, removals, reordering and grouped changes retain one bulk Reset.
				await bulkOperationSemaphore.WaitAsync(token);

				var filter = FilesAndFoldersFilter;
				var isSemaphoreReleased = false;
				try
				{
					// Snapshot under the semaphore so a late apply reflects the current list, not a stale pre-lock copy;
					// concurrent watcher removals (e.g. emptying the Recycle Bin) otherwise leave the last item painted.
					var filesAndFoldersLocal = filesAndFolders?.ToList() ?? new List<ListedItem>();

					var displayedFilesAndFolders = string.IsNullOrEmpty(filter)
						? filesAndFoldersLocal
						: await Task.Run(() => filesAndFoldersLocal.Where(x => MatchesFilter(x, filter)).ToList(), token);

					var dispatchRequested = Stopwatch.GetTimestamp();
					await dispatcherQueue.EnqueueOrInvokeAsync(() =>
					{
						var dispatchStarted = Stopwatch.GetTimestamp();
						try
						{
							if (token.IsCancellationRequested || FilesAndFoldersFilter != filter)
								return;

							pendingSearchResults.Clear();
							AppMemoryHelper.NotifyActivity();

							if (!TryPublishFilesAndFoldersWithoutReset(displayedFilesAndFolders, performanceOperation))
							{
								FilesAndFolders.BeginBulkOperation();
								try
								{
									FilesAndFolders.Clear();
									FilesAndFolders.AddRange(displayedFilesAndFolders);

									if (folderSettings.DirectoryGroupOption != GroupOption.None)
										OrderGroups();
								}
								finally
								{
									FilesAndFolders.EndBulkOperation();
								}
								if (UiPerformanceTrace.IsEnabled)
									UiPerformanceTrace.Mark(performanceOperation, "DataPublicationReset", FilesAndFolders.Count);
							}

							UpdateEmptyTextType();
							UpdateNetworkAvailabilityInfoBar();
							DirectoryInfoUpdated?.Invoke(this, EventArgs.Empty);
							UiPerformanceTrace.Mark(performanceOperation, "DataPublished", FilesAndFolders.Count,
								Stopwatch.GetElapsedTime(dispatchStarted).TotalMilliseconds);
							UiPerformanceTrace.Mark(performanceOperation, "PublicationQueueDelay", durationMs:
								Stopwatch.GetElapsedTime(dispatchRequested, dispatchStarted).TotalMilliseconds);
						}
						finally
						{
							isSemaphoreReleased = true;
							bulkOperationSemaphore.Release();
						}
					});

					// The semaphore will be released in UI thread
					isSemaphoreReleased = true;
				}
				finally
				{
					if (!isSemaphoreReleased)
						bulkOperationSemaphore.Release();
				}
			}
			catch (Exception ex)
			{
				// A newer load cancelled this one or disposed its token source, which is not worth reporting
				if (ex is not (OperationCanceledException or ObjectDisposedException))
					App.Logger.LogWarning(ex, ex.Message);
			}
		}

private bool TryPublishFilesAndFoldersWithoutReset(List<ListedItem> displayedFilesAndFolders, long performanceOperation)
		{
			if (FilesAndFolders.IsGrouped || folderSettings.DirectoryGroupOption != GroupOption.None)
				return false;
			var currentCount = FilesAndFolders.Count;
			if (currentCount > displayedFilesAndFolders.Count)
				return false;
			for (var index = 0; index < currentCount; index++)
				if (!ReferenceEquals(FilesAndFolders[index], displayedFilesAndFolders[index]))
					return false;

			if (currentCount == displayedFilesAndFolders.Count)
			{
				if (UiPerformanceTrace.IsEnabled)
					UiPerformanceTrace.Mark(performanceOperation, "DataPublicationUnchanged", currentCount);
				return true;
			}
			if (currentCount == 0)
				return false;

			var appendedCount = displayedFilesAndFolders.Count - currentCount;
			// WinUI requires individual Add events; bound the event burst before falling back to Reset.
			if (appendedCount > 128)
				return false;
			for (var index = currentCount; index < displayedFilesAndFolders.Count; index++)
				FilesAndFolders.Add(displayedFilesAndFolders[index]);
			if (UiPerformanceTrace.IsEnabled)
				UiPerformanceTrace.Mark(performanceOperation, "DataPublicationAppended", FilesAndFolders.Count, value: appendedCount);
			return true;
		}

private async Task OrderFilesAndFoldersAsync(CancellationToken cancellationToken = default)
		{
			using var orderCTS = CancellationTokenSource.CreateLinkedTokenSource(addFilesCTS.Token, cancellationToken);
			var token = orderCTS.Token;
			var sourceCollection = filesAndFolders;
			void OrderEntries()
			{
				if (token.IsCancellationRequested || sourceCollection.Count == 0)
					return;

				var ordered = new ConcurrentCollection<ListedItem>(SortingHelper.OrderFileList(sourceCollection.ToList(), folderSettings.DirectorySortOption, folderSettings.DirectorySortDirection,
					folderSettings.SortDirectoriesAlongsideFiles, folderSettings.SortFilesFirst));
				lock (loadCollectionSync)
				{
					if (!token.IsCancellationRequested && ReferenceEquals(filesAndFolders, sourceCollection))
						filesAndFolders = ordered;
				}
			}

			if (dispatcherQueue.HasThreadAccess)
				await Task.Run(OrderEntries, token);
			else
				OrderEntries();
		}

private async Task RapidAddItemsToCollectionAsync(string? path, string? previousDir, Action? postLoadCallback)
		{
			if (isDisposed)
				return;
			AppMemoryHelper.NotifyActivity();

			IsSearchResults = false;
			HasNoWatcher = false;
			IsLocationUnavailable = false;
			StopWatchingForLocationRestoration();
			ItemLoadStatusChanged?.Invoke(this, new ItemLoadStatusChangedEventArgs() { Status = ItemLoadStatusChangedEventArgs.ItemLoadStatus.Starting });

			CancelLoadAndClearFiles();
			var token = addFilesCTS.Token;
			var performanceOperation = UiPerformanceTrace.BeginOperation("Navigation", path ?? string.Empty);
			PerformanceOperationId = performanceOperation;

			if (string.IsNullOrEmpty(path))
				return;

			try
			{
				// Only one instance at a time should access this function
				// Wait here until the previous one has ended
				// If we're waiting and a new update request comes through simply drop this instance
				await enumFolderSemaphore.WaitAsync(token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			try
			{
				token.ThrowIfCancellationRequested();
				// Drop all the other waiting instances
				semaphoreCTS.Cancel();
				semaphoreCTS = new CancellationTokenSource();

				IsLoadingItems = true;
				UiPerformanceTrace.Mark(performanceOperation, "EnumerationStarted");

				filesAndFolders.Clear();
				desktopIniUpdateTask = null;

				ItemLoadStatusChanged?.Invoke(this, new ItemLoadStatusChangedEventArgs() { Status = ItemLoadStatusChangedEventArgs.ItemLoadStatus.InProgress });

				if (path.EndsWith(ShellLibraryItem.EXTENSION, StringComparison.OrdinalIgnoreCase))
				{
					if (App.LibraryManager.TryGetLibrary(path, out var library) && !library.IsEmpty)
					{
						var libItem = new LibraryItem(library);
						foreach (var folder in library.Folders)
							await RapidAddItemsToCollectionAsync(folder, token, libItem);
					}
				}
				else
				{
					await RapidAddItemsToCollectionAsync(path, token);
				}

				token.ThrowIfCancellationRequested();
				ItemLoadStatusChanged?.Invoke(this, new ItemLoadStatusChangedEventArgs() { Status = ItemLoadStatusChangedEventArgs.ItemLoadStatus.Complete, PreviousDirectory = previousDir, Path = path });
				IsLoadingItems = false;
				UiPerformanceTrace.Mark(performanceOperation, "ListingCompleted", FilesAndFolders.Count);

				if (Interlocked.Exchange(ref desktopIniUpdateTask, null) is Task task)
					await task;

				token.ThrowIfCancellationRequested();
				var adaptiveLayoutStarted = Stopwatch.GetTimestamp();
				AdaptiveLayoutHelpers.ApplyAdaptativeLayout(folderSettings, filesAndFolders.ToList());
				UiPerformanceTrace.Mark(performanceOperation, "AdaptiveLayoutCompleted", FilesAndFolders.Count,
					Stopwatch.GetElapsedTime(adaptiveLayoutStarted).TotalMilliseconds);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				return;
			}
			finally
			{
				// Make sure item count is updated
				if (!token.IsCancellationRequested)
					DirectoryInfoUpdated?.Invoke(this, EventArgs.Empty);
				enumFolderSemaphore.Release();
			}

			postLoadCallback?.Invoke();
		}

public void RefreshItems(string? previousDir, Action? postLoadCallback = null)
		{
			_ = RapidAddItemsToCollectionAsync(WorkingDirectory, previousDir, postLoadCallback);
		}

public async Task SearchAsync(FolderSearch search)
		{
			AppMemoryHelper.NotifyActivity();
			ItemLoadStatusChanged?.Invoke(this, new ItemLoadStatusChangedEventArgs() { Status = ItemLoadStatusChangedEventArgs.ItemLoadStatus.Starting });

			CancelSearch();
			using var currentSearchCTS = CancellationTokenSource.CreateLinkedTokenSource(addFilesCTS.Token);
			searchCTS = currentSearchCTS;
			var token = currentSearchCTS.Token;
			var performanceOperation = UiPerformanceTrace.BeginOperation("Search", WorkingDirectory ?? string.Empty);
			PerformanceOperationId = performanceOperation;
			try
			{
				filesAndFolders.Clear();
				IsLoadingItems = true;
				IsSearchResults = true;
				HasNoWatcher = true;
				await ApplyFilesAndFoldersChangesAsync(token);
				if (token.IsCancellationRequested)
					return;
				EmptyTextType = EmptyTextType.None;

				SearchHeaderTitle = !string.IsNullOrEmpty(search.Query)
					? string.Format(Strings.SearchResultsFor.GetLocalizedResource(), search.Query)
					: string.Empty;

				if (SearchIconBitmapImage is null)
					SearchIconBitmapImage ??= await UIHelpers.GetSearchIconResource();
				if (token.IsCancellationRequested)
					return;

				ItemLoadStatusChanged?.Invoke(this, new ItemLoadStatusChangedEventArgs() { Status = ItemLoadStatusChangedEventArgs.ItemLoadStatus.InProgress });

				// Stops late ticks from appending after the final apply
				using var tickCTS = CancellationTokenSource.CreateLinkedTokenSource(token);
				var tickToken = tickCTS.Token;
				void Search_SearchTick(object? sender, IReadOnlyList<ListedItem> newItems) => _ = AppendSearchResultsAsync(newItems, tickToken);

				var results = new List<ListedItem>();
				search.DispatcherQueue = dispatcherQueue;
				search.SearchTick += Search_SearchTick;
				try
				{
					UiPerformanceTrace.Mark(performanceOperation, "ProviderStarted");
					await search.SearchAsync(results, token);
					UiPerformanceTrace.Mark(performanceOperation, "ProviderCompleted", results.Count);
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested)
				{
					return;
				}
				finally
				{
					search.SearchTick -= Search_SearchTick;
					tickCTS.Cancel();
				}

				try
				{
					lock (loadCollectionSync)
					{
						token.ThrowIfCancellationRequested();
						filesAndFolders = new ConcurrentCollection<ListedItem>(results);
					}

					await OrderFilesAndFoldersAsync(token);
					await ApplyFilesAndFoldersChangesAsync(token);
					token.ThrowIfCancellationRequested();
					if (search.HasIncompleteResults)
						SearchHeaderTitle += $" — {Strings.SearchResultsIncomplete.GetLocalizedResource()}";
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested)
				{
					return;
				}

				// A newer search owns the loading state
				if (ReferenceEquals(searchCTS, currentSearchCTS))
				{
					ItemLoadStatusChanged?.Invoke(this, new ItemLoadStatusChangedEventArgs() { Status = ItemLoadStatusChangedEventArgs.ItemLoadStatus.Complete });
					IsLoadingItems = false;
					UiPerformanceTrace.Mark(performanceOperation, "SearchCompleted", FilesAndFolders.Count);
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
			}
			finally
			{
				if (ReferenceEquals(searchCTS, currentSearchCTS))
				{
					if (token.IsCancellationRequested && IsSearchResults)
					{
						IsLoadingItems = false;
						UiPerformanceTrace.Mark(performanceOperation, "Canceled");
					}
					searchCTS = null;
				}
			}
		}

private async Task AppendSearchResultsAsync(IReadOnlyList<ListedItem> newItems, CancellationToken token)
		{
			var performanceOperation = PerformanceOperationId;
			try
			{
				await bulkOperationSemaphore.WaitAsync(token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			try
			{
				await dispatcherQueue.EnqueueOrInvokeAsync(() =>
				{
					if (token.IsCancellationRequested)
						return;
					var dispatchStarted = Stopwatch.GetTimestamp();

					filesAndFolders.AddRange(newItems);
					foreach (var item in newItems)
						pendingSearchResults.Enqueue(item);

					var filter = FilesAndFoldersFilter;
					var appendedCount = 0;
					while (appendedCount < MaxSearchResultsAppendedPerTick && pendingSearchResults.TryDequeue(out var item))
					{
						if (!MatchesFilter(item, filter))
							continue;

						FilesAndFolders.Add(item);
						appendedCount++;
					}

					UpdateEmptyTextType();
					DirectoryInfoUpdated?.Invoke(this, EventArgs.Empty);
					UiPerformanceTrace.Mark(performanceOperation, "DataPublished", FilesAndFolders.Count,
						Stopwatch.GetElapsedTime(dispatchStarted).TotalMilliseconds);
				});
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, ex.Message);
			}
			finally
			{
				bulkOperationSemaphore.Release();
			}
		}

private async Task PreloadIconsAsync(IEnumerable<ListedItem> items, CancellationToken cancellationToken = default)
		{
			var iconSize = GetPreloadIconSize();
			foreach (var item in items)
			{
				cancellationToken.ThrowIfCancellationRequested();
				item.PreloadedIconData ??= await iconCacheService.GetIconAsync(item.ItemPath, item.IsFolder ? null : item.FileExtension, item.IsFolder, iconSize);
			}
		}

public void CancelSearch()
		{
			searchCTS?.Cancel();
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
}}
