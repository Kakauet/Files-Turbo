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
						item.cFileName.EndsWith(ShellLibraryItem.EXTENSION, StringComparison.OrdinalIgnoreCase) ||
						NativeNameSearch.IsShortcut(item.cFileName))
						return null;

					var name = item.cFileName;
					if (isFile && !showExtensions)
					{
						var withoutExtension = Path.GetFileNameWithoutExtension(name);
						if (!string.IsNullOrEmpty(withoutExtension))
							name = withoutExtension;
					}
					var modified = FromFileTime(item.ftLastWriteTime);
					var created = FromFileTime(item.ftCreationTime);
					var label = formatter?.ToTimeSpanLabel(group == GroupOption.DateCreated ? created : modified, dateUnit);
					entries.Add(new(item, entries.Count, name, isFile, modified, created, isFile ? item.GetSize() : 0,
						label?.Index ?? 0, label?.Text));
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
