// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Files.App.Utils.Shell
{
	/// <summary>
	/// Creates the shell items of one file operation. Parsing a full path takes about 1.5 ms, which made queuing
	/// a thousand items cost seconds; a name below an already parsed folder takes a few microseconds.
	/// </summary>
	/// <remarks>Folders stay alive until the factory is disposed; use it on the operation's STA thread.</remarks>
	internal sealed class ShellItemFactory : IDisposable
	{
		private readonly Dictionary<string, ShellFolder> folders = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>The folder at <paramref name="path"/>, parsed once per factory.</summary>
		public ShellFolder GetFolder(string path)
		{
			if (!folders.TryGetValue(path, out var folder))
				folders[path] = folder = new ShellFolder(path);
			return folder;
		}

		/// <summary>The item at <paramref name="path"/>; the caller disposes it.</summary>
		public ShellItem CreateItem(string path)
		{
			if (ShellParsePath.TrySplit(path, out var folderPath, out var name))
			{
				ShellFolder? folder = null;
				try
				{
					folder = GetFolder(folderPath);
				}
				catch (Exception)
				{
					// The full parse below reports the error as before
				}

				if (folder is not null && PInvoke.SHCreateItemFromRelativeName(folder.IShellItem, name, null!, out IShellItem item).Succeeded)
					return ShellItem.Wrap(item);
			}

			return new ShellItem(path);
		}

		public void Dispose()
		{
			foreach (var folder in folders.Values)
				folder.Dispose();
			folders.Clear();
		}
	}
}
