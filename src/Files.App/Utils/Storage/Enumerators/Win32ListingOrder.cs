// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Runtime.InteropServices.ComTypes;

namespace Files.App.Utils.Storage
{
	internal static class Win32ListingOrder
	{
		private sealed record Entry(Win32PInvoke.WIN32_FIND_DATA Data, string Name, bool IsFile,
			DateTimeOffset Modified, DateTimeOffset Created, long Size, int GroupIndex, string? GroupText);

		internal static DateTime FromFileTime(FILETIME time)
		{
			var ticks = ((long)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
			return DateTime.FromFileTimeUtc(ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond);
		}

		internal static List<Win32PInvoke.WIN32_FIND_DATA>? TryOrder(List<Win32PInvoke.WIN32_FIND_DATA> data,
			SortOption sort, SortDirection direction, bool alongside, bool filesFirst, GroupOption group,
			SortDirection groupDirection, GroupByDateUnit dateUnit, bool showExtensions, CancellationToken token)
		{
			if (sort is not (SortOption.Name or SortOption.DateModified or SortOption.DateCreated or SortOption.Size) ||
				group is not (GroupOption.None or GroupOption.DateModified or GroupOption.DateCreated))
				return null;

			var entries = new List<Entry>(data.Count);
			var formatter = group == GroupOption.None ? null : Ioc.Default.GetRequiredService<IDateTimeFormatter>();
			try
			{
				foreach (var item in data)
				{
					token.ThrowIfCancellationRequested();
					var attributes = (FileAttributes)item.dwFileAttributes;
					var isFile = (attributes & FileAttributes.Directory) == 0;
					// These names are supplied by the shell rather than the directory record.
					if ((!isFile && (attributes & (FileAttributes.ReadOnly | FileAttributes.System)) != 0) ||
						item.cFileName.EndsWith(ShellLibraryItem.EXTENSION, StringComparison.OrdinalIgnoreCase) ||
						NativeNameSearch.IsShortcut(item.cFileName))
						return null;

					var name = item.cFileName;
					var shortcut = isFile && NativeNameSearch.IsShortcut(name) &&
						item.dwReserved0 != Win32PInvoke.IO_REPARSE_TAG_SYMLINK;
					if (shortcut || (isFile && !showExtensions))
					{
						var withoutExtension = Path.GetFileNameWithoutExtension(name);
						if (shortcut || !string.IsNullOrEmpty(withoutExtension))
							name = withoutExtension;
					}
					var modified = FromFileTime(item.ftLastWriteTime);
					var created = FromFileTime(item.ftCreationTime);
					var label = formatter?.ToTimeSpanLabel(group == GroupOption.DateCreated ? created : modified, dateUnit);
					entries.Add(new(item, name, isFile, modified, created, isFile ? item.GetSize() : 0,
						label?.Index ?? 0, label?.Text));
				}
			}
			catch (ArgumentException)
			{
				return null;
			}

			object? SortKey(Entry item) => sort switch
			{
				SortOption.DateModified => item.Modified,
				SortOption.DateCreated => item.Created,
				SortOption.Size => item.Size,
				_ => item.Name
			};
			var natural = NaturalStringComparer.GetForProcessor();
			var comparer = sort == SortOption.Name ? natural : Comparer<object?>.Default;
			IOrderedEnumerable<Entry> ordered;
			if (group != GroupOption.None)
			{
				ordered = groupDirection == SortDirection.Ascending
					? entries.OrderBy(x => x.GroupIndex).ThenBy(x => x.GroupText)
					: entries.OrderByDescending(x => x.GroupIndex).ThenByDescending(x => x.GroupText);
				if (!alongside)
					ordered = ordered.ThenBy(x => x.IsFile ^ filesFirst);
			}
			else
			{
				ordered = entries.OrderBy(x => !alongside && (x.IsFile ^ filesFirst));
			}
			ordered = direction == SortDirection.Ascending
				? ordered.ThenBy(SortKey, comparer)
				: ordered.ThenByDescending(SortKey, comparer);
			if (sort != SortOption.Name)
				ordered = direction == SortDirection.Ascending
					? ordered.ThenBy(x => x.Name, natural)
					: ordered.ThenByDescending(x => x.Name, natural);

			token.ThrowIfCancellationRequested();
			return ordered.Select(x => x.Data).ToList();
		}
	}
}
