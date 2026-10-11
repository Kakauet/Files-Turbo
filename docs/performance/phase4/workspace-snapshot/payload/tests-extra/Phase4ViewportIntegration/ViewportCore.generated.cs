// Extracted literally from production scheduler handoff methods.
#nullable enable
namespace Files.App.Views.Layouts { public partial class BaseLayoutPage {
private bool NeedsDeferredProperties(ListedItem item)
		{
			if (!item.ItemPropertiesInitialized)
				return true;
			var enabledGit = ParentShellPageInstance?.ShellViewModel?.EnabledGitProperties ?? GitProperties.None;
			return item is IGitItem gitItem &&
				(enabledGit is (GitProperties.All or GitProperties.Status) && !gitItem.StatusPropertiesInitialized ||
				enabledGit is (GitProperties.All or GitProperties.Commit) && !gitItem.CommitPropertiesInitialized);
		}

private void QueueItemProperties(SelectorItem container, ListedItem item, ShellViewModel shellViewModel, int priority = 0)
		{
			if (!IsCurrentContainer(container, item) || shellViewModel.IsScrollInFlight)
				return;
			if (thumbnailFailures.GetValueOrDefault(item) >= 2)
				return;
			var (first, last) = GetVisibleIndexRange();
			var index = ItemsControl.IndexFromContainer(container);
			priority = index < 0 || first >= 0 && (index < first || index > last) ? int.MaxValue : index - Math.Max(0, first);
			if (viewportThumbnails.TryGetValue(item, out var thumbnail))
			{
				if (shellViewModel.IsViewportThumbnailCurrent(item, thumbnail))
				{
					QueueItemMetadata(container, item, shellViewModel, thumbnail, priority);
					return;
				}
				viewportThumbnails.Remove(item);
				metadataFailures.Remove(item);
				thumbnailFailures.Remove(item);
				viewportWork?.Cancel(item);
				metadataWork?.Cancel(item);
				shellViewModel.CancelExtendedPropertiesLoadingForItem(item);
			}

			viewportWork ??= new ViewportWorkQueue<ListedItem>(4, 128,
				action => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => action()),
				RefillVisibleItemWork,
				exception => App.Logger.LogWarning(exception, "Failed to load deferred row properties."), newestFirst: false);
			viewportWork.SetPaused(isReloadingRealizedItems || shellViewModel.IsScrollInFlight);
			var performanceOperation = shellViewModel.PerformanceOperationId;
			viewportWork.Enqueue(item, async token =>
			{
				if (!IsCurrentContainer(container, item) ||
					!ReferenceEquals(ParentShellPageInstance?.ShellViewModel, shellViewModel))
					return;

				var started = UiPerformanceTrace.IsEnabled ? Stopwatch.GetTimestamp() : 0;
				if (started != 0)
					UiPerformanceTrace.Mark(performanceOperation, "ItemJobStarted", viewportWork?.ActiveCount ?? 0,
						value: viewportWork?.PendingCount ?? 0);
				try
				{
					var initialThumbnail = item.ItemPropertiesInitialized ? default : await shellViewModel.LoadViewportThumbnailAsync(item, token);
					token.ThrowIfCancellationRequested();
					if (initialThumbnail.LoadCompleted)
						viewportThumbnails[item] = initialThumbnail;
					thumbnailFailures.Remove(item);
					QueueItemMetadata(container, item, shellViewModel, initialThumbnail, priority);
					if (started != 0)
						UiPerformanceTrace.Mark(performanceOperation, "ItemJobCompleted", durationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds);
				}
				catch (OperationCanceledException)
				{
					if (!token.IsCancellationRequested && IsCurrentContainer(container, item))
						thumbnailFailures[item] = thumbnailFailures.GetValueOrDefault(item) + 1;
					if (started != 0)
						UiPerformanceTrace.Mark(performanceOperation, "ItemJobCancelled", durationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds);
					throw;
				}
				catch (Exception) when (!token.IsCancellationRequested && IsCurrentContainer(container, item))
				{
					thumbnailFailures[item] = thumbnailFailures.GetValueOrDefault(item) + 1;
					throw;
				}
			}, priority, (int)shellViewModel.EnabledGitProperties);
		}

private void QueueItemMetadata(SelectorItem container, ListedItem item, ShellViewModel shellViewModel,
			ShellViewModel.InitialThumbnailLoadResult initialThumbnail, int priority)
		{
			if (metadataFailures.GetValueOrDefault(item) >= 2)
				return;
			metadataWork ??= new ViewportWorkQueue<ListedItem>(4, 128,
				action => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => action()),
				RefillVisibleItemWork,
				faulted: exception => App.Logger.LogWarning(exception, "Failed to load deferred row metadata."), newestFirst: false);
			metadataWork.SetPaused(isReloadingRealizedItems || shellViewModel.IsScrollInFlight);
			metadataWork.Enqueue(item, async token =>
			{
				if (!IsCurrentContainer(container, item) || !ReferenceEquals(ParentShellPageInstance?.ShellViewModel, shellViewModel))
					return;
				try
				{
					token.ThrowIfCancellationRequested();
					if (!item.ItemPropertiesInitialized)
						await shellViewModel.LoadExtendedItemPropertiesAsync(item, token, initialThumbnail);
					token.ThrowIfCancellationRequested();
					if (!item.ItemPropertiesInitialized)
						throw new OperationCanceledException();
					if (shellViewModel.EnabledGitProperties is not GitProperties.None && item is IGitItem gitItem)
						await shellViewModel.LoadGitPropertiesAsync(gitItem, token);
					metadataFailures.Remove(item);
				}
				catch (Exception) when (!token.IsCancellationRequested && IsCurrentContainer(container, item))
				{
					metadataFailures[item] = metadataFailures.GetValueOrDefault(item) + 1;
					throw;
				}
			}, priority, (int)shellViewModel.EnabledGitProperties);
		}

private void RefillVisibleItemWork()
		{
			if (isDisposed || navigationArguments is null || viewportRefillQueued || viewportWork is { PendingCount: > 0 } ||
				ParentShellPageInstance?.ShellViewModel is not { IsScrollInFlight: false } shellViewModel)
				return;
			viewportRefillQueued = true;
			var performanceOperation = shellViewModel.PerformanceOperationId;
			var requestedAt = UiPerformanceTrace.IsEnabled ? Stopwatch.GetTimestamp() : 0;
			if (requestedAt != 0)
				UiPerformanceTrace.Mark(performanceOperation, "ViewportPassQueued", realizedItems.Count);
			if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
			{
				viewportRefillQueued = false;
				if (!isDisposed && navigationArguments is not null && shellViewModel.IsScrollInFlight is false &&
					ReferenceEquals(ParentShellPageInstance?.ShellViewModel, shellViewModel))
				{
					var startedAt = requestedAt != 0 ? Stopwatch.GetTimestamp() : 0;
					if (startedAt != 0)
						UiPerformanceTrace.Mark(performanceOperation, "ViewportPassStarted", realizedItems.Count,
							durationMs: Stopwatch.GetElapsedTime(requestedAt, startedAt).TotalMilliseconds);
					LoadVisibleItemsAfterScrollSettled();
					if (startedAt != 0)
						UiPerformanceTrace.Mark(performanceOperation, "ViewportPassCompleted", realizedItems.Count,
							durationMs: Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
				}
			}))
				viewportRefillQueued = false;
		}

protected void LoadVisibleItemsAfterScrollSettled()
		{
			if (ParentShellPageInstance?.ShellViewModel is not { } shellViewModel)
				return;

			var (first, last) = GetVisibleIndexRange();
			if (first >= 0 && last >= first)
			{
				for (var i = first; i <= last; i++)
				{
					if (ItemsControl.ContainerFromIndex(i) is SelectorItem { Content: ListedItem item } container && NeedsDeferredProperties(item))
					{
						realizedItems.TryAdd(container, item);
						QueueItemProperties(container, item, shellViewModel);
					}
				}
			}
			else
			{
				// A missing visible range must not restore XAML's realization order.
				foreach (var (container, item) in realizedItems.OrderBy(pair => ItemsControl.IndexFromContainer(pair.Key)))
				{
					if (ItemsControl.IndexFromContainer(container) >= 0 && IsCurrentContainer(container, item) && NeedsDeferredProperties(item))
						QueueItemProperties(container, item, shellViewModel);
				}
			}
		}

private void ReprioritizeViewportWork()
		{
			if (viewportWork is null)
				return;
			var (first, last) = GetVisibleIndexRange();
			if (first < 0 || last < first)
				return;

			foreach (var (container, item) in realizedItems)
			{
				var index = ItemsControl.IndexFromContainer(container);
				if (index >= first && index <= last)
				{
					viewportWork.SetPriority(item, index - first);
					metadataWork?.SetPriority(item, index - first);
				}
				else
				{
					viewportWork.Cancel(item);
					metadataWork?.Cancel(item);
					viewportThumbnails.Remove(item);
					metadataFailures.Remove(item);
					thumbnailFailures.Remove(item);
				}
			}
		}

protected Task ReloadRealizedItemPropertiesAsync()
		{
			if (ParentShellPageInstance?.ShellViewModel is not { } shellViewModel)
				return Task.CompletedTask;

			viewportWork?.CancelAll();
			metadataWork?.CancelAll();
			viewportWork?.SetPaused(true);
			metadataWork?.SetPaused(true);
			viewportThumbnails.Clear();
			metadataFailures.Clear();
			thumbnailFailures.Clear();
			shellViewModel.CancelExtendedPropertiesLoading();
			foreach (var item in shellViewModel.FilesAndFolders)
				item.ItemPropertiesInitialized = false;

			isReloadingRealizedItems = true;
			try
			{
				var (first, last) = GetVisibleIndexRange();
				foreach (var (container, item) in realizedItems.OrderBy(pair => ItemsControl.IndexFromContainer(pair.Key)))
				{
					var index = ItemsControl.IndexFromContainer(container);
					var priority = first >= 0 && (index < first || index > last) ? 1 : 0;
					QueueItemProperties(container, item, shellViewModel, priority);
				}
				LoadVisibleItemsAfterScrollSettled();
			}
			finally
			{
				isReloadingRealizedItems = false;
				viewportWork?.SetPaused(shellViewModel.IsScrollInFlight);
				metadataWork?.SetPaused(shellViewModel.IsScrollInFlight);
			}
			return Task.CompletedTask;
		}

private void HookScrollDeferTracking()
		{
			if (performanceViewProbe is null && UiPerformanceTrace.IsEnabled)
				performanceViewProbe = new UiPerformanceViewProbe(ItemsControl,
					() => ParentShellPageInstance?.ShellViewModel?.PerformanceOperationId ?? 0, GetVisibleIndexRange);
			if (deferScrollViewer is not null)
				return;

			deferScrollViewer = ItemsControl.FindDescendant<ScrollViewer>();
			if (deferScrollViewer is not null)
			{
				lastScrollHorizontalOffset = deferScrollViewer.HorizontalOffset;
				lastScrollVerticalOffset = deferScrollViewer.VerticalOffset;
				lastScrollZoomFactor = deferScrollViewer.ZoomFactor;
				deferScrollViewer.ViewChanged += DeferScrollViewer_ViewChanged;
			}
		}

private void UnhookScrollDeferTracking()
		{
			scrollSettleTimer?.Stop();
			viewportWork?.Dispose();
			viewportWork = null;
			metadataWork?.Dispose();
			metadataWork = null;
			performanceViewProbe?.Dispose();
			performanceViewProbe = null;
			realizedItems.Clear();
			viewportThumbnails.Clear();
			metadataFailures.Clear();
			thumbnailFailures.Clear();
			ParentShellPageInstance?.ShellViewModel?.NotifyScrollStateChanged(false);

			if (deferScrollViewer is not null)
			{
				deferScrollViewer.ViewChanged -= DeferScrollViewer_ViewChanged;
				deferScrollViewer = null;
			}
		}

private void DeferScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
		{
			if (isDisposed || sender is not ScrollViewer scrollViewer || !ReferenceEquals(scrollViewer, deferScrollViewer))
				return;
			var moved = scrollViewer.HorizontalOffset != lastScrollHorizontalOffset ||
				scrollViewer.VerticalOffset != lastScrollVerticalOffset || scrollViewer.ZoomFactor != lastScrollZoomFactor;
			lastScrollHorizontalOffset = scrollViewer.HorizontalOffset;
			lastScrollVerticalOffset = scrollViewer.VerticalOffset;
			lastScrollZoomFactor = scrollViewer.ZoomFactor;
			var shellViewModel = ParentShellPageInstance?.ShellViewModel;
			if (!moved)
			{
				if (shellViewModel?.IsScrollInFlight is true && !e.IsIntermediate)
				{
					// The final event may repeat the last offset; it still closes the gesture's debounce window.
					if (UiPerformanceTrace.IsEnabled)
						UiPerformanceTrace.Mark(shellViewModel.PerformanceOperationId, "ScrollFinalWithoutMovement", realizedItems.Count);
					scrollSettleTimer?.Stop();
					scrollSettleTimer?.Start();
				}
				else if (shellViewModel?.IsScrollInFlight is false)
				{
					if (UiPerformanceTrace.IsEnabled)
						UiPerformanceTrace.Mark(shellViewModel.PerformanceOperationId, "ScrollGeometryIgnored", realizedItems.Count);
					RefillVisibleItemWork();
				}
				return;
			}
			viewportWork?.SetPaused(true);
			metadataWork?.SetPaused(true);
			ReprioritizeViewportWork();
			shellViewModel?.NotifyScrollStateChanged(true);
			if (UiPerformanceTrace.IsEnabled)
				UiPerformanceTrace.Mark(shellViewModel?.PerformanceOperationId ?? 0, "ScrollDeferred", realizedItems.Count, value: e.IsIntermediate ? 1 : 0);
			AppMemoryHelper.NotifyActivity();

			if (scrollSettleTimer is null)
			{
				scrollSettleTimer = DispatcherQueue.CreateTimer();
				scrollSettleTimer.Interval = TimeSpan.FromMilliseconds(200);
				scrollSettleTimer.IsRepeating = false;
				scrollSettleTimer.Tick += (_, _) =>
				{
					if (isDisposed || deferScrollViewer is null)
						return;
					ParentShellPageInstance?.ShellViewModel?.NotifyScrollStateChanged(false);
					if (UiPerformanceTrace.IsEnabled)
						UiPerformanceTrace.Mark(ParentShellPageInstance?.ShellViewModel?.PerformanceOperationId ?? 0, "ScrollSettled", realizedItems.Count);
					metadataFailures.Clear();
					thumbnailFailures.Clear();
					LoadVisibleItemsAfterScrollSettled();
					viewportWork?.SetPaused(false);
					metadataWork?.SetPaused(false);
				};
			}

			// Debounce real movement; extent-only changes must not postpone initial image work.
			scrollSettleTimer.Stop();
			scrollSettleTimer.Start();
			if (UiPerformanceTrace.IsEnabled)
				UiPerformanceTrace.Mark(shellViewModel?.PerformanceOperationId ?? 0, "ScrollSettleScheduled", realizedItems.Count, value: e.IsIntermediate ? 1 : 0);
		}

private void RefreshItem(SelectorItem container, object item, bool inRecycleQueue, ContainerContentChangingEventArgs args)
		{
			if (inRecycleQueue)
			{
				UpdateItemToolTip(container, null);
				if (realizedItems.Remove(container, out var recycledItem))
				{
					viewportWork?.Cancel(recycledItem);
					metadataWork?.Cancel(recycledItem);
					viewportThumbnails.Remove(recycledItem);
					metadataFailures.Remove(recycledItem);
					thumbnailFailures.Remove(recycledItem);
					var shellViewModel = ParentShellPageInstance.GetRequiredShellViewModel();
					shellViewModel.CancelExtendedPropertiesLoadingForItem(recycledItem);
					shellViewModel.ReleaseExtendedProperties(recycledItem);
				}
				return;
			}

			if (item is ListedItem listedItem)
			{
				if (args.Phase == 0 && listedItem.IsRealChanges)
					listedItem.UpdateReal();
				if (realizedItems.TryGetValue(container, out var previous) && !ReferenceEquals(previous, listedItem))
				{
					viewportWork?.Cancel(previous);
					metadataWork?.Cancel(previous);
					viewportThumbnails.Remove(previous);
					metadataFailures.Remove(previous);
					thumbnailFailures.Remove(previous);
					var shellViewModel = ParentShellPageInstance.GetRequiredShellViewModel();
					shellViewModel.CancelExtendedPropertiesLoadingForItem(previous);
					shellViewModel.ReleaseExtendedProperties(previous);
				}
				realizedItems[container] = listedItem;
				if (args.Phase == 0 && NeedsDeferredProperties(listedItem))
				{
					if (UiPerformanceTrace.IsEnabled)
						UiPerformanceTrace.Mark(ParentShellPageInstance?.ShellViewModel?.PerformanceOperationId ?? 0,
							"ViewportWorkRequested", realizedItems.Count, value: args.ItemIndex);
					RefillVisibleItemWork();
				}
				UpdateItemToolTip(container, listedItem.ItemTooltipText);
				InitializeDrag(container, listedItem);

				if (NeedsDeferredProperties(listedItem))
				{
					uint callbackPhase = 3;
					args.RegisterUpdateCallback(callbackPhase, (s, c) =>
					{
						if (c.InRecycleQueue || !ReferenceEquals(c.Item, listedItem) ||
							!ReferenceEquals(c.ItemContainer, container) || !IsCurrentContainer(container, listedItem))
							return;
						var shellViewModel = ParentShellPageInstance.GetRequiredShellViewModel();

						// Rows realized mid-scroll are mostly flung past before the gesture ends; the settle pass loads the ones still visible
						if (shellViewModel.IsScrollInFlight)
							return;

						RefillVisibleItemWork();
					});
				}
			}
		}

private bool IsCurrentContainer(SelectorItem container, ListedItem item)
			=> !isDisposed && navigationArguments is not null && realizedItems.TryGetValue(container, out var current) &&
				ReferenceEquals(current, item) && ReferenceEquals(container.Content, item);
} }
