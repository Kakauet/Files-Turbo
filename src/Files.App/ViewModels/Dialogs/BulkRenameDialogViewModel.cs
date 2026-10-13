// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Storage;

namespace Files.App.ViewModels.Dialogs
{
	public sealed partial class BulkRenameDialogViewModel : ObservableObject
	{
		private IContentPageContext context { get; } = Ioc.Default.GetRequiredService<IContentPageContext>();

		// Properties

		public bool IsNameValid =>
			FilesystemHelpers.IsValidForFilename(_FileName) && !_FileName.Contains('.');

		public bool ShowNameWarning =>
			!string.IsNullOrEmpty(_FileName) && !IsNameValid;

		private string _FileName = string.Empty;
		public string FileName
		{
			get => _FileName;
			set
			{
				if (SetProperty(ref _FileName, value))
				{
					OnPropertyChanged(nameof(IsNameValid));
					OnPropertyChanged(nameof(ShowNameWarning));
				}
			}
		}

		// Commands

		public IAsyncRelayCommand CommitRenameCommand { get; private set; }

		public BulkRenameDialogViewModel()
		{
			CommitRenameCommand = new AsyncRelayCommand(DoCommitRenameAsync);
		}

		private async Task DoCommitRenameAsync()
		{
			if (context.ShellPage is null)
				return;

			var selected = context.SelectedItems.ToList();
			var pending = await RenamePlainItemsAsync(selected);

			foreach (ListedItem item in pending)
			{
				var itemType = item.PrimaryItemAttribute == StorageItemTypes.Folder ? FilesystemItemType.Directory : FilesystemItemType.File;
				await context.ShellPage.FilesystemHelpers.RenameAsync(
					StorageHelpers.FromPathAndType(item.GetRequiredPath(), itemType),
					FileName + item.FileExtension,
					NameCollisionOption.GenerateUniqueName,
					true,
					false
				);
			}
			;
		}

		/// <summary>
		/// Renames plain file system items in one shell operation, in selection order and with the same collision
		/// handling as one rename per item, which took about 80 ms per item. Each item still gets its own history entry.
		/// </summary>
		/// <returns>The items left for the per-item path: all of them for other item types, else those that failed.</returns>
		private async Task<List<ListedItem>> RenamePlainItemsAsync(List<ListedItem> selected)
		{
			if (selected.Count < 2 || !selected.All(IsPlainFileSystemItem))
				return selected;

			var paths = selected.Select(x => x.GetRequiredPath()).ToArray();
			var names = selected.Select(x => FileName + x.FileExtension).ToArray();
			var (_, result) = await FileOperationsHelpers.RenameItemsAsync(paths, names, MainWindow.Instance.WindowHandle.ToInt64());

			var renamed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
			foreach (var item in result.Final)
			{
				if (item is { Succeeded: true, Source: { } source })
					renamed[source] = item.Destination;
			}

			var jumpListService = Ioc.Default.GetRequiredService<IWindowsJumpListService>();
			var pending = new List<ListedItem>();
			for (var index = 0; index < selected.Count; index++)
			{
				// The path captured before renaming: the watcher may already have updated the item to its new name
				var item = selected[index];
				var path = paths[index];
				if (!renamed.TryGetValue(path, out var destination))
				{
					// The per-item path shows upstream's dialogs for permissions, items in use or name conflicts
					pending.Add(item);
					continue;
				}

				var itemType = item.PrimaryItemAttribute == StorageItemTypes.Folder ? FilesystemItemType.Directory : FilesystemItemType.File;
				if (destination is not null && destination != path)
				{
					App.HistoryWrapper.AddHistory(new StorageHistory(FileOperationType.Rename,
						StorageHelpers.FromPathAndType(path, itemType), StorageHelpers.FromPathAndType(destination, itemType)));
				}

				if (itemType != FilesystemItemType.File)
					await jumpListService.RemoveFolderAsync(path);
			}

			return pending;
		}

		/// <summary>An existing local or network file or folder, addressed by a plain path (no FTP, archive, recycle bin or stream).</summary>
		private static bool IsPlainFileSystemItem(ListedItem item)
			=> item is not FtpItem and not ZipItem and not AlternateStreamItem &&
				item.ItemPath is { } path &&
				path.Length > 3 && SystemIO.Path.IsPathFullyQualified(path) &&
				path.IndexOf(':', 2) < 0 &&
				!FtpHelpers.IsFtpPath(path) &&
				!path.StartsWith(Constants.UserEnvironmentPaths.RecycleBinPath, StringComparison.OrdinalIgnoreCase) &&
				(SystemIO.File.Exists(path) || SystemIO.Directory.Exists(path));
	}
}
