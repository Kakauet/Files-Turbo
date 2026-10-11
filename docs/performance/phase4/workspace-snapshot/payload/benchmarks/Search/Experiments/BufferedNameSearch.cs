// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.IO.Enumeration;

namespace Files.App.Utils.Storage
{
	internal readonly record struct LocalSearchEntry(string Name, string Path, FileAttributes Attributes, long Length,
		DateTimeOffset Created, DateTimeOffset Modified, bool IsMatch);

	internal static class LocalSearchEnumerable
	{
		internal static IEnumerable<LocalSearchEntry> Enumerate(string folder, string expression, bool showHidden,
			bool showSystem, bool showDotFiles, bool hiddenOnly, CancellationToken token)
		{
			bool IsMatch(ref FileSystemEntry entry)
			{
				var name = entry.FileName;
				var attributes = entry.Attributes;
				var hidden = (attributes & FileAttributes.Hidden) != 0;
				var system = (attributes & FileAttributes.System) != 0;
				var shortcut = !entry.IsDirectory && (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
					|| name.EndsWith(".url", StringComparison.OrdinalIgnoreCase));
				return (hiddenOnly ? (!hidden && shortcut) || (hidden && showHidden && (!system || showSystem))
					: !hidden || (showHidden && (!system || showSystem)))
					&& (name.IsEmpty || name[0] != '.' || showDotFiles)
					&& FileSystemName.MatchesWin32Expression(expression, name);
			}

			return new FileSystemEnumerable<LocalSearchEntry>(folder,
				(ref FileSystemEntry entry) => new(entry.FileName.ToString(), entry.ToFullPath(), entry.Attributes,
					entry.Length, entry.CreationTimeUtc, entry.LastWriteTimeUtc, !entry.IsDirectory || IsMatch(ref entry)),
				new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, BufferSize = 64 * 1024 })
			{
				ShouldIncludePredicate = (ref FileSystemEntry entry) =>
				{
					token.ThrowIfCancellationRequested();
					return entry.IsDirectory || IsMatch(ref entry);
				},
			};
		}
	}
}
