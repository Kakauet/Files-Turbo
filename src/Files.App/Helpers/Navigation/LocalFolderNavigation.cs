// Copyright (c) Files Community
// Licensed under the MIT License.

using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Helpers
{
	internal static class LocalFolderNavigation
	{
		internal static bool CanProbe(string path)
			=> path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' &&
				path.AsSpan(3).Trim('\\').Length > 0;

		internal static Task<bool> IsAccessibleAsync(string path)
			=> !CanProbe(path) ? Task.FromResult(false) : Task.Run(() =>
			{
				var attributes = Win32Helper.GetFileAttributes(path);
				return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory &&
					FolderHelpers.CheckFolderAccessWithWin32(path);
			});
	}
}
