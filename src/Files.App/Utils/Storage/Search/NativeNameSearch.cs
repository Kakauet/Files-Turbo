// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Utils.Storage
{
	internal readonly record struct NameSearchEntry(string Name, string Path, FileAttributes Attributes, long Length,
		DateTimeOffset Created, DateTimeOffset Modified, bool IsMatch, uint ReparseTag);

	internal static class NativeNameSearch
	{
		internal static IEnumerable<NameSearchEntry> Enumerate(string folder, string expression, bool showHidden,
			bool showSystem, bool showDotFiles, bool hiddenOnly, CancellationToken token, Action? onIncomplete = null)
		{
			token.ThrowIfCancellationRequested();
			using var handle = FindFirst(folder, out var data);
			if (handle.IsInvalid)
			{
				onIncomplete?.Invoke();
				yield break;
			}

			do
			{
				token.ThrowIfCancellationRequested();
				var attributes = (FileAttributes)data.dwFileAttributes;
				var isDirectory = (attributes & FileAttributes.Directory) != 0;
				var buffer = data.cFileName.AsSpan();
				var length = buffer.IndexOf('\0');
				ReadOnlySpan<char> name = length >= 0 ? buffer[..length] : buffer;
				if (name.SequenceEqual(".") || name.SequenceEqual(".."))
					continue;

				var match = ShouldInclude(name, expression, attributes, showHidden, showSystem, showDotFiles, hiddenOnly);
				if (!isDirectory && !match)
					continue;

				var fileName = name.ToString();
				yield return new(fileName, Path.Combine(folder, fileName), attributes,
					((long)data.nFileSizeHigh << 32) | data.nFileSizeLow,
					FromFileTime(data.ftCreationTime), FromFileTime(data.ftLastWriteTime), match, data.dwReserved0);
			}
			while (PInvoke.FindNextFile(handle, out data));

			if (Marshal.GetLastPInvokeError() != (int)WIN32_ERROR.ERROR_NO_MORE_FILES)
				onIncomplete?.Invoke();
		}

		internal static bool ShouldInclude(ReadOnlySpan<char> name, string expression, FileAttributes attributes,
			bool showHidden, bool showSystem, bool showDotFiles, bool hiddenOnly)
		{
			var hidden = (attributes & FileAttributes.Hidden) != 0;
			var system = (attributes & FileAttributes.System) != 0;
			var isDirectory = (attributes & FileAttributes.Directory) != 0;
			return (hiddenOnly ? (!hidden && !isDirectory && IsShortcut(name)) || (hidden && showHidden && (!system || showSystem))
				: !hidden || (showHidden && (!system || showSystem)))
				&& (name.IsEmpty || name[0] != '.' || showDotFiles)
				&& FileSystemName.MatchesWin32Expression(expression, name);
		}

		internal static bool IsShortcut(ReadOnlySpan<char> name)
			=> name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

		internal static string GetResultPathKey(string path, string? root)
		{
			if (string.IsNullOrEmpty(root) || path.Length <= root.Length || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
				|| (!Path.EndsInDirectorySeparator(root) && path[root.Length] != Path.DirectorySeparatorChar && path[root.Length] != Path.AltDirectorySeparatorChar)
				|| path.AsSpan(0, root.Length).SequenceEqual(root))
				return path;

			return string.Concat(root.AsSpan(), path.AsSpan(root.Length));
		}

		private static DateTimeOffset FromFileTime(System.Runtime.InteropServices.ComTypes.FILETIME time)
			=> DateTimeOffset.FromFileTime(((long)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime);

		private static unsafe FindCloseSafeHandle FindFirst(string folder, out WIN32_FIND_DATAW data)
		{
			WIN32_FIND_DATAW local = default;
			var handle = PInvoke.FindFirstFileEx(Path.Join(folder, "*"), FINDEX_INFO_LEVELS.FindExInfoBasic,
				&local, FINDEX_SEARCH_OPS.FindExSearchNameMatch, FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
			data = local;
			return handle;
		}
	}
}
