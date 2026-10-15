// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services.SizeProvider;
using Files.Shared.Helpers;
using System.IO;
using Windows.Storage;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Utils.Storage
{
	public static class Win32StorageEnumerator
	{
		private static readonly ISizeProvider folderSizeProvider = Ioc.Default.GetRequiredService<ISizeProvider>();
		private static readonly IStorageCacheService fileListCache = Ioc.Default.GetRequiredService<IStorageCacheService>();

		private static readonly string folderTypeTextLocalized = Strings.Folder.GetLocalizedResource();

		// Rows in the first publication: more than a maximized Details view shows
		private const int FirstBatchSize = 64;

		public static async Task<List<ListedItem>> ListEntries(
			string path,
			Win32PInvoke.SafeFindHandle hFile,
			Win32PInvoke.WIN32_FIND_DATA findData,
			CancellationToken cancellationToken,
			int countLimit,
			uint iconSize,
			Func<List<ListedItem>, Task> intermediateAction,
			Task<bool>? repositoryState = null,
			Func<List<Win32PInvoke.WIN32_FIND_DATA>, IEnumerable<Win32PInvoke.WIN32_FIND_DATA>?>? prepareOrder = null,
			int firstBatchSize = FirstBatchSize
		)
		{
			var sampler = new IntervalSampler(500);
			Task publication = Task.CompletedTask;
			var publishedCount = 0;
			firstBatchSize = Math.Clamp(firstBatchSize, FirstBatchSize, 512);
			var tempList = new List<ListedItem>();
			var count = 0;

			IUserSettingsService userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
			bool CalculateFolderSizes = userSettingsService.FoldersSettingsService.CalculateFolderSizes;
			bool showHiddenItems = userSettingsService.FoldersSettingsService.ShowHiddenItems;
			bool showProtectedSystemFiles = userSettingsService.FoldersSettingsService.ShowProtectedSystemFiles;
			bool showDotFiles = userSettingsService.FoldersSettingsService.ShowDotFiles;
			bool areAlternateStreamsVisible = userSettingsService.FoldersSettingsService.AreAlternateStreamsVisible;

			var isGitRepo = false;
			var rawHandle = hFile.DangerousGetHandle();

			try
			{
				var buffered = new List<Win32PInvoke.WIN32_FIND_DATA>();
				IEnumerable<Win32PInvoke.WIN32_FIND_DATA> entries = buffered;
				var complete = false;
				if (prepareOrder is not null && countLimit < 0)
				{
					var scanStarted = Stopwatch.GetTimestamp();
					do
					{
						cancellationToken.ThrowIfCancellationRequested();
						buffered.Add(findData);
						if (!Win32PInvoke.FindNextFile(rawHandle, out findData))
						{
							complete = true;
							break;
						}
					} while (buffered.Count < 200_000 && Stopwatch.GetElapsedTime(scanStarted).TotalMilliseconds < 150);
					if (complete)
					{
						buffered.RemoveAll(x => x.cFileName is "." or ".." ||
							(((FileAttributes)x.dwFileAttributes & FileAttributes.Hidden) != 0 &&
								(!showHiddenItems || !showProtectedSystemFiles && ((FileAttributes)x.dwFileAttributes & FileAttributes.System) != 0)) ||
							(!showDotFiles && x.cFileName.StartsWith('.')));
						if (prepareOrder(buffered) is { } sorted)
							entries = sorted;
					}
				}
				isGitRepo = repositoryState is not null && await repositoryState.WaitAsync(cancellationToken);
				// Counted from here so the pre-scan does not shrink the first batch to a single row
				var firstBatchSampler = new IntervalSampler(25);
				foreach (var entry in ReadEntries(entries, complete, rawHandle, findData))
				{
					findData = entry;
					cancellationToken.ThrowIfCancellationRequested();
					var isSystem = ((FileAttributes)findData.dwFileAttributes & FileAttributes.System) == FileAttributes.System;
					var isHidden = ((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) == FileAttributes.Hidden;
					var startWithDot = findData.cFileName.StartsWith('.');
					if ((!isHidden ||
						(showHiddenItems &&
							(!isSystem || showProtectedSystemFiles))) &&
						(!startWithDot || showDotFiles))
					{
						if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) != FileAttributes.Directory)
						{
							var file = await GetFile(findData, path, isGitRepo, cancellationToken);
							if (file is not null)
							{
								file.Win32Attributes = (FileAttributes)findData.dwFileAttributes;
								var filePath = file.ItemPath!;
								tempList.Add(file);
								++count;

								if (areAlternateStreamsVisible)
									tempList.AddRange(EnumAdsForPath(filePath, file));
							}
						}
						else if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
						{
							if (findData.cFileName != "." && findData.cFileName != "..")
							{
								var folder = await GetFolder(findData, path, isGitRepo, cancellationToken);
								if (folder is not null)
								{
									folder.Win32Attributes = (FileAttributes)findData.dwFileAttributes;
									var folderPath = folder.ItemPath!;
									tempList.Add(folder);
									++count;

									if (areAlternateStreamsVisible)
										tempList.AddRange(EnumAdsForPath(folderPath, folder));

									if (CalculateFolderSizes)
									{
										if (folderSizeProvider.TryGetSize(folderPath, out var size))
										{
											folder.FileSizeBytes = (long)size;
											folder.FileSize = size.ToSizeString();
										}

										_ = folderSizeProvider.UpdateAsync(folderPath, cancellationToken);
									}
								}
							}
						}
					}

					cancellationToken.ThrowIfCancellationRequested();
					if (count == countLimit)
						break;

					// The first batch is one viewport of rows (in final order when the listing was pre-sorted), or whatever
					// slow storage produced in 25 ms. Later batches follow a fixed cadence: each publication copies the
					// whole listing, so a per-count cadence would make loading large folders quadratic.
					if (intermediateAction is not null &&
						(publishedCount >= firstBatchSize
							? sampler.CheckNow()
							: tempList.Count >= firstBatchSize - publishedCount || tempList.Count > 0 && firstBatchSampler.CheckNow()))
					{
						publishedCount += tempList.Count;
						var batch = tempList;
						tempList = new();
						publication = publication.ContinueWith(async previous =>
						{
							await previous.ConfigureAwait(false);
							cancellationToken.ThrowIfCancellationRequested();
							await intermediateAction(batch).ConfigureAwait(false);
						}, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
					}
				}
			}
			finally
			{
				hFile.Dispose();
			}

			await publication.WaitAsync(cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			return tempList;
		}

		private static IEnumerable<Win32PInvoke.WIN32_FIND_DATA> ReadEntries(IEnumerable<Win32PInvoke.WIN32_FIND_DATA> buffered,
			bool complete, nint handle, Win32PInvoke.WIN32_FIND_DATA first)
		{
			foreach (var entry in buffered)
				yield return entry;
			if (complete)
				yield break;
			do
			{
				yield return first;
			} while (Win32PInvoke.FindNextFile(handle, out first));
		}

		private static IEnumerable<ListedItem> EnumAdsForPath(string itemPath, ListedItem main)
		{
			foreach (var ads in Win32Helper.GetAlternateStreams(itemPath))
				yield return GetAlternateStream(ads, main);
		}

		public static ListedItem GetAlternateStream((string Name, long Size) ads, ListedItem main)
		{
			string itemType = Strings.File.GetLocalizedResource();
			string? itemFileExtension = null;

			if (ads.Name.Contains('.'))
			{
				itemFileExtension = Path.GetExtension(ads.Name);
				itemType = itemFileExtension!.Trim('.') + " " + itemType;
			}

			string adsName = ads.Name.Substring(1, ads.Name.Length - 7); // Remove ":" and ":$DATA"

			return new AlternateStreamItem()
			{
				PrimaryItemAttribute = StorageItemTypes.File,
				FileExtension = itemFileExtension,
				FileImage = null,
				LoadFileIcon = false,
				ItemNameRaw = adsName,
				IsHiddenItem = false,
				Opacity = Constants.UI.DimItemOpacity,
				ItemDateModifiedReal = main.ItemDateModifiedReal,
				ItemDateAccessedReal = main.ItemDateAccessedReal,
				ItemDateCreatedReal = main.ItemDateCreatedReal,
				ItemType = itemType,
				ItemPath = $"{main.ItemPath}:{adsName}",
				FileSize = ads.Size.ToSizeString(),
				FileSizeBytes = ads.Size
			};
		}

		public static async Task<ListedItem?> GetFolder(
			Win32PInvoke.WIN32_FIND_DATA findData,
			string pathRoot,
			bool isGitRepo,
			CancellationToken cancellationToken
		)
		{
			if (cancellationToken.IsCancellationRequested)
				return null;

			DateTime itemModifiedDate;
			DateTime itemCreatedDate;

			try
			{
				itemModifiedDate = Win32ListingOrder.FromFileTime(findData.ftLastWriteTime);
				itemCreatedDate = Win32ListingOrder.FromFileTime(findData.ftCreationTime);
			}
			catch (ArgumentException)
			{
				// Invalid date means invalid findData, do not add to list
				return null;
			}

			var itemPath = Path.Combine(pathRoot, findData.cFileName);

			string itemName = await fileListCache.GetDisplayName(itemPath, cancellationToken);
			if (string.IsNullOrEmpty(itemName))
			{
				itemName = findData.cFileName;

				// The shell only reads desktop.ini for folders marked ReadOnly or System, and keeps the raw name for junctions
				const FileAttributes desktopIniFlags = FileAttributes.ReadOnly | FileAttributes.System;
				var attributes = (FileAttributes)findData.dwFileAttributes;
				if ((attributes & desktopIniFlags) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
					itemName = Win32Helper.GetLocalizedName(itemPath) ?? itemName;
			}

			bool isHidden = (((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) == FileAttributes.Hidden);
			double opacity = 1;

			if (isHidden)
				opacity = Constants.UI.DimItemOpacity;

			if (isGitRepo)
			{
				return new GitItem()
				{
					PrimaryItemAttribute = StorageItemTypes.Folder,
					ItemNameRaw = itemName,
					ItemDateModifiedReal = itemModifiedDate,
					ItemDateCreatedReal = itemCreatedDate,
					ItemType = folderTypeTextLocalized,
					FileImage = null,
					IsHiddenItem = isHidden,
					Opacity = opacity,
					LoadFileIcon = false,
					ItemPath = itemPath,
					FileSize = null,
					FileSizeBytes = 0,
				};
			}
			else
			{
				return new ListedItem(null)
				{
					PrimaryItemAttribute = StorageItemTypes.Folder,
					ItemNameRaw = itemName,
					ItemDateModifiedReal = itemModifiedDate,
					ItemDateCreatedReal = itemCreatedDate,
					ItemType = folderTypeTextLocalized,
					FileImage = null,
					IsHiddenItem = isHidden,
					Opacity = opacity,
					LoadFileIcon = false,
					ItemPath = itemPath,
					FileSize = null,
					FileSizeBytes = 0,
				};
			}
		}

		public static async Task<ListedItem?> GetFile(
			Win32PInvoke.WIN32_FIND_DATA findData,
			string pathRoot,
			bool isGitRepo,
			CancellationToken cancellationToken
		)
		{
			if (cancellationToken.IsCancellationRequested)
				return null;

			var itemPath = Path.Combine(pathRoot, findData.cFileName);
			var itemName = findData.cFileName;

			DateTime itemModifiedDate, itemCreatedDate, itemLastAccessDate;

			try
			{
				itemModifiedDate = Win32ListingOrder.FromFileTime(findData.ftLastWriteTime);
				itemCreatedDate = Win32ListingOrder.FromFileTime(findData.ftCreationTime);
				itemLastAccessDate = Win32ListingOrder.FromFileTime(findData.ftLastAccessTime);
			}
			catch (ArgumentException)
			{
				// Invalid date means invalid findData, do not add to list
				return null;
			}

			long itemSizeBytes = findData.GetSize();
			var itemSize = itemSizeBytes.ToSizeString();
			string itemType = Strings.File.GetLocalizedResource();
			string? itemFileExtension = null;

			if (findData.cFileName.Contains('.'))
			{
				itemFileExtension = Path.GetExtension(itemPath);

				// Resolve the localized type here (cached by extension) so it's correct from the first paint and sorts right
				var localizedType = FileTypesHelper.GetLocalizedTypeName(itemFileExtension);
				itemType = !string.IsNullOrEmpty(localizedType)
					? localizedType
					: itemFileExtension!.Trim('.') + " " + itemType;
			}

			bool itemThumbnailImgVis = false;

			if (cancellationToken.IsCancellationRequested)
				return null;

			bool isHidden = ((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) == FileAttributes.Hidden;
			double opacity = isHidden ? Constants.UI.DimItemOpacity : 1;

			// https://learn.microsoft.com/openspecs/windows_protocols/ms-fscc/c8e77b37-3909-4fe6-a4ea-2b9d423b1ee4
			bool isReparsePoint = ((FileAttributes)findData.dwFileAttributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
			bool isSymlink = isReparsePoint && findData.dwReserved0 == Win32PInvoke.IO_REPARSE_TAG_SYMLINK;

			if (isSymlink)
			{
				var targetPath = Win32Helper.ParseSymLink(itemPath);
				if (isGitRepo)
				{
					return new GitShortcutItem()
					{
						PrimaryItemAttribute = StorageItemTypes.File,
						FileExtension = itemFileExtension,
						IsHiddenItem = isHidden,
						Opacity = opacity,
						FileImage = null,
						LoadFileIcon = itemThumbnailImgVis,
						ItemNameRaw = itemName,
						ItemDateModifiedReal = itemModifiedDate,
						ItemDateAccessedReal = itemLastAccessDate,
						ItemDateCreatedReal = itemCreatedDate,
						ItemType = Strings.Shortcut.GetLocalizedResource(),
						ItemPath = itemPath,
						FileSize = itemSize,
						FileSizeBytes = itemSizeBytes,
						TargetPath = targetPath,
						IsSymLink = true,
					};
				}
				else
				{
					return new ShortcutItem(null)
					{
						PrimaryItemAttribute = StorageItemTypes.File,
						FileExtension = itemFileExtension,
						IsHiddenItem = isHidden,
						Opacity = opacity,
						FileImage = null,
						LoadFileIcon = itemThumbnailImgVis,
						ItemNameRaw = itemName,
						ItemDateModifiedReal = itemModifiedDate,
						ItemDateAccessedReal = itemLastAccessDate,
						ItemDateCreatedReal = itemCreatedDate,
						ItemType = Strings.Shortcut.GetLocalizedResource(),
						ItemPath = itemPath,
						FileSize = itemSize,
						FileSizeBytes = itemSizeBytes,
						TargetPath = targetPath,
						IsSymLink = true
					};
				}
			}
			else if (string.Equals(itemFileExtension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(itemFileExtension, ".url", StringComparison.OrdinalIgnoreCase))
			{
				var isUrl = string.Equals(itemFileExtension, ".url", StringComparison.OrdinalIgnoreCase);

				// Listing only needs the data stored in the link file; resolving the target
				// can block on moved or unreachable targets and is done when the item is opened
				var shInfo = await FileOperationsHelpers.ParseLinkAsync(itemPath, resolveTarget: false);
				if (shInfo is null)
					return null;

				if (isGitRepo)
				{
					return new GitShortcutItem()
					{
						PrimaryItemAttribute = shInfo.IsFolder ? StorageItemTypes.Folder : StorageItemTypes.File,
						FileExtension = itemFileExtension,
						IsHiddenItem = isHidden,
						Opacity = opacity,
						FileImage = null,
						LoadFileIcon = !shInfo.IsFolder && itemThumbnailImgVis,
						ItemNameRaw = itemName,
						ItemDateModifiedReal = itemModifiedDate,
						ItemDateAccessedReal = itemLastAccessDate,
						ItemDateCreatedReal = itemCreatedDate,
						ItemType = isUrl ? Strings.ShortcutWebLinkFileType.GetLocalizedResource() : Strings.Shortcut.GetLocalizedResource(),
						ItemPath = itemPath,
						FileSize = itemSize,
						FileSizeBytes = itemSizeBytes,
						TargetPath = shInfo.TargetPath,
						Arguments = shInfo.Arguments,
						WorkingDirectory = shInfo.WorkingDirectory,
						RunAsAdmin = shInfo.RunAsAdmin,
						ShowWindowCommand = shInfo.ShowWindowCommand,
						IsUrl = isUrl,
					};
				}
				else
				{
					return new ShortcutItem(null)
					{
						PrimaryItemAttribute = shInfo.IsFolder ? StorageItemTypes.Folder : StorageItemTypes.File,
						FileExtension = itemFileExtension,
						IsHiddenItem = isHidden,
						Opacity = opacity,
						FileImage = null,
						LoadFileIcon = !shInfo.IsFolder && itemThumbnailImgVis,
						ItemNameRaw = itemName,
						ItemDateModifiedReal = itemModifiedDate,
						ItemDateAccessedReal = itemLastAccessDate,
						ItemDateCreatedReal = itemCreatedDate,
						ItemType = isUrl ? Strings.ShortcutWebLinkFileType.GetLocalizedResource() : Strings.Shortcut.GetLocalizedResource(),
						ItemPath = itemPath,
						FileSize = itemSize,
						FileSizeBytes = itemSizeBytes,
						TargetPath = shInfo.TargetPath,
						Arguments = shInfo.Arguments,
						WorkingDirectory = shInfo.WorkingDirectory,
						RunAsAdmin = shInfo.RunAsAdmin,
						ShowWindowCommand = shInfo.ShowWindowCommand,
						IsUrl = isUrl,
					};
				}
			}
			else if (App.LibraryManager.TryGetLibrary(itemPath, out var library))
			{
				return new LibraryItem(library)
				{
					Opacity = opacity,
					ItemDateModifiedReal = itemModifiedDate,
					ItemDateCreatedReal = itemCreatedDate,
				};
			}
			else
			{
				if (ZipStorageFolder.IsZipPath(itemPath) && await ZipStorageFolder.CheckDefaultZipApp(itemPath))
				{
					return new ZipItem(null)
					{
						PrimaryItemAttribute = StorageItemTypes.Folder, // Treat zip files as folders
						FileExtension = itemFileExtension,
						FileImage = null,
						LoadFileIcon = itemThumbnailImgVis,
						ItemNameRaw = itemName,
						IsHiddenItem = isHidden,
						Opacity = opacity,
						ItemDateModifiedReal = itemModifiedDate,
						ItemDateAccessedReal = itemLastAccessDate,
						ItemDateCreatedReal = itemCreatedDate,
						ItemType = itemType,
						ItemPath = itemPath,
						FileSize = itemSize,
						FileSizeBytes = itemSizeBytes
					};
				}
				else if (isGitRepo)
				{
					return new GitItem()
					{
						PrimaryItemAttribute = StorageItemTypes.File,
						FileExtension = itemFileExtension,
						FileImage = null,
						LoadFileIcon = itemThumbnailImgVis,
						ItemNameRaw = itemName,
						IsHiddenItem = isHidden,
						Opacity = opacity,
						ItemDateModifiedReal = itemModifiedDate,
						ItemDateAccessedReal = itemLastAccessDate,
						ItemDateCreatedReal = itemCreatedDate,
						ItemType = itemType,
						ItemPath = itemPath,
						FileSize = itemSize,
						FileSizeBytes = itemSizeBytes
					};
				}
				else
				{
					return new ListedItem(null)
					{
						PrimaryItemAttribute = StorageItemTypes.File,
						FileExtension = itemFileExtension,
						FileImage = null,
						LoadFileIcon = itemThumbnailImgVis,
						ItemNameRaw = itemName,
						IsHiddenItem = isHidden,
						Opacity = opacity,
						ItemDateModifiedReal = itemModifiedDate,
						ItemDateAccessedReal = itemLastAccessDate,
						ItemDateCreatedReal = itemCreatedDate,
						ItemType = itemType,
						ItemPath = itemPath,
						FileSize = itemSize,
						FileSizeBytes = itemSizeBytes
					};
				}
			}
		}
	}
}
