// Production methods extracted literally; collection implementations are linked without edits.
#nullable enable
namespace Files.App.ViewModels { public partial class ShellViewModel {
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

private static bool MatchesFilter(ListedItem item, string? filter)
			=> string.IsNullOrEmpty(filter) || item.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
} }
internal static class PublicationBuildStamp {
    public const string SourceSha256 = "270CC6DB226256E8C5D40E169E8D1339EE4AE02130025C1086AE1C1879FFB3ED";
    public const string LinkedFingerprint = "28EB2E9EA698C32636CF6C0CADDB1D4E6A7AF6D3F27C7D32C033DDD5E8420BA8";
}
