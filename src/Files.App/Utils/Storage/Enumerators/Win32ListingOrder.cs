// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.Runtime.InteropServices.ComTypes;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Orders raw directory records the way <see cref="SortingHelper.OrderFileList"/> orders the listed items,
	/// so a large folder's first rows can be published in their final position before the rest is sorted.
	/// </summary>
	internal static class Win32ListingOrder
	{
		private const int PrefixSize = 64;

		internal sealed record Entry(Win32PInvoke.WIN32_FIND_DATA Data, int Index, string Name, bool IsFile,
			DateTimeOffset Modified, DateTimeOffset Created, long Size, int GroupIndex, string? GroupText);

		internal static DateTime FromFileTime(FILETIME time)
		{
			var ticks = ((long)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
			return DateTime.FromFileTimeUtc(ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond);
		}

		internal static IEnumerable<Win32PInvoke.WIN32_FIND_DATA>? TryOrder(List<Win32PInvoke.WIN32_FIND_DATA> data,
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
						item.cFileName.EndsWith(ShellLibraryItem.EXTENSION, StringComparison.OrdinalIgnoreCase))
						return null;

					entries.Add(CreateEntry(item, entries.Count, GetDisplayName(item, isFile, showExtensions), isFile, formatter, group, dateUnit));
				}
			}
			catch (ArgumentException)
			{
				return null;
			}

			var compare = CreateComparison(sort, direction, alongside, filesFirst, group, groupDirection);
			token.ThrowIfCancellationRequested();
			if (entries.Count > 2_000)
				return OrderedPrefix.Read(entries, compare, PrefixSize, token).Select(entry => entry.Data);

			entries.Sort(compare);
			return entries.Select(entry => entry.Data).ToList();
		}

		/// <summary>
		/// Whether items listed in a prepared order are in the order <see cref="SortingHelper.OrderFileList"/> gives them,
		/// judged on the values the items display; a mismatch means the list must be sorted again.
		/// </summary>
		internal static bool IsInOrder(IReadOnlyList<(string Name, bool IsFile, DateTimeOffset Modified, DateTimeOffset Created, long Size)> items,
			SortOption sort, SortDirection direction, bool alongside, bool filesFirst, GroupOption group,
			SortDirection groupDirection, GroupByDateUnit dateUnit)
		{
			var formatter = group == GroupOption.None ? null : Ioc.Default.GetRequiredService<IDateTimeFormatter>();
			var compare = CreateComparison(sort, direction, alongside, filesFirst, group, groupDirection);
			Entry? previous = null;
			for (var index = 0; index < items.Count; index++)
			{
				var item = items[index];
				var label = formatter?.ToTimeSpanLabel(group == GroupOption.DateCreated ? item.Created : item.Modified, dateUnit);
				var entry = new Entry(default, index, item.Name, item.IsFile, item.Modified, item.Created, item.Size, label?.Index ?? 0, label?.Text);
				if (previous is not null && compare(previous, entry) > 0)
					return false;
				previous = entry;
			}
			return true;
		}

		private static Entry CreateEntry(Win32PInvoke.WIN32_FIND_DATA item, int index, string name, bool isFile,
			IDateTimeFormatter? formatter, GroupOption group, GroupByDateUnit dateUnit)
		{
			var modified = FromFileTime(item.ftLastWriteTime);
			var created = FromFileTime(item.ftCreationTime);
			var label = formatter?.ToTimeSpanLabel(group == GroupOption.DateCreated ? created : modified, dateUnit);
			return new(item, index, name, isFile, modified, created, isFile ? item.GetSize() : 0, label?.Index ?? 0, label?.Text);
		}

		// The name ListedItem displays: shortcuts always hide their extension, other files follow the setting
		private static string GetDisplayName(Win32PInvoke.WIN32_FIND_DATA item, bool isFile, bool showExtensions)
		{
			var name = item.cFileName;
			if (!isFile)
				return name;

			var attributes = (FileAttributes)item.dwFileAttributes;
			var isSymlink = (attributes & FileAttributes.ReparsePoint) != 0 && item.dwReserved0 == Win32PInvoke.IO_REPARSE_TAG_SYMLINK;
			if (!isSymlink && NativeNameSearch.IsShortcut(name))
				return Path.GetFileNameWithoutExtension(name);

			if (!showExtensions)
			{
				var withoutExtension = Path.GetFileNameWithoutExtension(name);
				if (!string.IsNullOrEmpty(withoutExtension))
					return withoutExtension;
			}
			return name;
		}

		/// <summary>
		/// The order of <see cref="SortingHelper.OrderFileList"/> (group, folders or files first, sort key, name),
		/// with the original position last, as LINQ's stable sort would leave ties.
		/// </summary>
		internal static Comparison<Entry> CreateComparison(SortOption sort, SortDirection direction, bool alongside,
			bool filesFirst, GroupOption group, SortDirection groupDirection)
		{
			var natural = NaturalStringComparer.GetForProcessor();
			var descending = direction == SortDirection.Descending;
			var groupDescending = groupDirection == SortDirection.Descending;
			return (x, y) =>
			{
				int result;
				if (group != GroupOption.None)
				{
					result = x.GroupIndex.CompareTo(y.GroupIndex);
					if (result == 0)
						result = string.Compare(x.GroupText, y.GroupText, StringComparison.CurrentCulture);
					if (result != 0)
						return groupDescending ? -result : result;
				}

				if (!alongside)
				{
					result = (x.IsFile ^ filesFirst).CompareTo(y.IsFile ^ filesFirst);
					if (result != 0)
						return result;
				}

				result = sort switch
				{
					SortOption.DateModified => x.Modified.CompareTo(y.Modified),
					SortOption.DateCreated => x.Created.CompareTo(y.Created),
					SortOption.Size => x.Size.CompareTo(y.Size),
					_ => natural.Compare(x.Name, y.Name),
				};
				if (result == 0 && sort != SortOption.Name)
					result = natural.Compare(x.Name, y.Name);
				if (result != 0)
					return descending ? -result : result;

				return x.Index.CompareTo(y.Index);
			};
		}
	}
}
