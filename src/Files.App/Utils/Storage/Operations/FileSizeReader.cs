// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Utils.Storage.Operations
{
	internal static class FileSizeReader
	{
		/// <summary>
		/// Reads a file's size from its attributes without opening it, so a delete or move running at the same time
		/// never meets a handle held for progress reporting.
		/// </summary>
		public static bool TryGetSize(string path, out long size)
		{
			var info = new FileInfo(path);
			size = info.Exists ? info.Length : 0;
			return info.Exists;
		}
	}
}
