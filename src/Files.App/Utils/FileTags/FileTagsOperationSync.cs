// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.FileTags
{
	internal enum FileTagsOperation
	{
		Copy,
		Move,
		Rename,
		Delete,
	}

	/// <summary>
	/// The tag database and file system calls needed to keep tags with items that a shell operation copies, moves,
	/// renames or deletes.
	/// </summary>
	internal interface IFileTagsSyncTarget
	{
		/// <summary>Whether the database holds an entry for <paramref name="path"/>; a registry lookup, no file access.</summary>
		bool HasEntry(string path);

		string[] GetTags(string path);

		void SetTags(string path, ulong? frn, string[] tags);

		void MoveEntry(string oldPath, ulong? frn, string newPath);

		IReadOnlyList<(string Path, string[] Tags)> GetAllUnderPath(string folderPath);

		/// <summary>Opens the file to read its file reference number; the expensive call this type avoids.</summary>
		ulong? GetFrn(string path);

		bool IsFolder(string path);

		void WriteFolderTags(string path, string[] tags);
	}

	/// <summary>
	/// Applies one completed item of a shell file operation to the tag database.
	/// </summary>
	/// <remarks>
	/// Upstream opened every destination file to read its FRN, inside the copy engine's progress callback, even
	/// when nothing was tagged. Untagged items have nothing to carry over, so they now cost one registry lookup
	/// instead of a file open (which also lets real-time antivirus scan each new file before the copy continues).
	/// Tagged items are handled exactly as before.
	/// </remarks>
	internal static class FileTagsOperationSync
	{
		/// <summary>
		/// Where the item went: the item the copy engine created when it reports one, else the requested name in the
		/// destination folder (the source folder for a rename). With rename-on-collision the requested name ("x.txt")
		/// differs from the created one ("x (2).txt"). A delete has a destination only when the item was recycled.
		/// </summary>
		public static string? ResolveDestination(FileTagsOperation operation, string? sourcePath, string? destinationFolder, string? requestedName, string? createdItemPath)
		{
			if (operation == FileTagsOperation.Delete || !string.IsNullOrEmpty(createdItemPath))
				return string.IsNullOrEmpty(createdItemPath) ? null : createdItemPath;

			var folder = operation == FileTagsOperation.Rename ? (sourcePath is null ? null : System.IO.Path.GetDirectoryName(sourcePath)) : destinationFolder;
			return folder is not null && !string.IsNullOrEmpty(requestedName) ? System.IO.Path.Combine(folder, requestedName) : null;
		}

		public static void Apply(IFileTagsSyncTarget target, FileTagsOperation operation, string sourcePath, string? destination,
			bool childrenNotProcessed, Action<Exception>? onError = null)
		{
			if (destination is null)
			{
				target.SetTags(sourcePath, null, []); // remove tag from deleted files
			}
			else
			{
				try
				{
					if (operation == FileTagsOperation.Copy)
					{
						var tags = target.GetTags(sourcePath);
						// An untagged copy only matters when it replaced a tagged item
						if (tags.Length > 0 || target.HasEntry(destination))
						{
							target.SetTags(destination, target.GetFrn(destination), tags); // copy tag to new files
							if (target.IsFolder(destination)) // File tag is not copied automatically for folders
								target.WriteFolderTags(destination, tags);
						}
					}
					else if (target.HasEntry(sourcePath))
					{
						target.MoveEntry(sourcePath, target.GetFrn(destination), destination); // move tag to new files
					}
				}
				catch (Exception ex)
				{
					onError?.Invoke(ex);
				}
			}

			if (!childrenNotProcessed) // child items are processed one by one otherwise
				return;

			var children = target.GetAllUnderPath(sourcePath);
			foreach (var (childPath, childTags) in children)
			{
				try
				{
					if (destination is null) // remove tag for items contained in the folder
					{
						target.SetTags(childPath, null, []);
						continue;
					}

					var subPath = childPath.Replace(sourcePath, destination, StringComparison.Ordinal);
					if (operation == FileTagsOperation.Copy) // copy tag for items contained in the folder
						target.SetTags(subPath, target.GetFrn(subPath), childTags);
					else // move tag to new files
						target.MoveEntry(childPath, target.GetFrn(subPath), subPath);
				}
				catch (Exception ex) when (destination is not null)
				{
					onError?.Invoke(ex);
				}
			}
		}
	}
}
