// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices.ComTypes;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class ListingOrderTests
	{
		private static readonly string[] Stems = ["file2", "file10", "File1", "report", "Report 3", "café", "zeta", "Ångström", "a", "b10c", "b9c", "日本"];
		private static readonly string[] Extensions = [".txt", ".log", "", ".TXT", ".dat"];

		private static FILETIME FileTime(DateTime utc)
		{
			var ticks = utc.ToFileTimeUtc();
			return new FILETIME { dwHighDateTime = (int)(ticks >> 32), dwLowDateTime = (int)(ticks & 0xFFFFFFFF) };
		}

		// A folder's records; names are unique as on disk, but display names, dates and sizes repeat to exercise ties
		private static List<Win32PInvoke.WIN32_FIND_DATA> Records(int count, int seed)
		{
			var random = new Random(seed);
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var records = new List<Win32PInvoke.WIN32_FIND_DATA>();
			var origin = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			while (records.Count < count)
			{
				var folder = random.Next(5) == 0;
				var name = $"{Stems[random.Next(Stems.Length)]}{random.Next(count)}{(folder ? "" : Extensions[random.Next(Extensions.Length)])}";
				if (!names.Add(name))
					continue;
				records.Add(new()
				{
					cFileName = name,
					dwFileAttributes = (uint)(folder ? FileAttributes.Directory : FileAttributes.Archive),
					ftLastWriteTime = FileTime(origin.AddMinutes(random.Next(50))),
					ftCreationTime = FileTime(origin.AddMinutes(random.Next(50))),
					nFileSizeLow = (uint)random.Next(20),
				});
			}
			return records;
		}

		private sealed record Model(Win32PInvoke.WIN32_FIND_DATA Data, string Name, bool IsFile, DateTimeOffset Modified, DateTimeOffset Created, long Size);

		// The ordering of SortingHelper.OrderFileList, written independently with LINQ's stable sort
		private static List<string> Reference(List<Win32PInvoke.WIN32_FIND_DATA> records, SortOption sort, SortDirection direction, bool alongside, bool filesFirst, bool showExtensions)
		{
			var natural = NaturalStringComparer.GetForProcessor();
			var models = records.Select(record =>
			{
				var isFile = ((FileAttributes)record.dwFileAttributes & FileAttributes.Directory) == 0;
				var name = isFile && !showExtensions && Path.GetFileNameWithoutExtension(record.cFileName) is { Length: > 0 } stem ? stem : record.cFileName;
				return new Model(record, name, isFile, Win32ListingOrder.FromFileTime(record.ftLastWriteTime), Win32ListingOrder.FromFileTime(record.ftCreationTime), isFile ? record.nFileSizeLow : 0);
			}).ToList();

			Func<Model, object?> key = sort switch
			{
				SortOption.DateModified => m => m.Modified,
				SortOption.DateCreated => m => m.Created,
				SortOption.Size => m => m.Size,
				_ => m => m.Name,
			};
			var comparer = sort == SortOption.Name ? natural : Comparer<object?>.Default;
			bool Prior(Model m) => m.IsFile ^ filesFirst;

			IOrderedEnumerable<Model> ordered = direction == SortDirection.Ascending
				? alongside ? models.OrderBy(key, comparer) : models.OrderBy(Prior).ThenBy(key, comparer)
				: alongside ? models.OrderByDescending(key, comparer) : models.OrderBy(Prior).ThenByDescending(key, comparer);
			if (sort != SortOption.Name)
				ordered = direction == SortDirection.Ascending ? ordered.ThenBy(m => m.Name, natural) : ordered.ThenByDescending(m => m.Name, natural);
			return ordered.Select(m => m.Data.cFileName).ToList();
		}

		[TestMethod]
		public void Listing_order_matches_the_app_sort_for_every_option()
		{
			foreach (var count in new[] { 1500, 5000 })
			{
				var records = Records(count, count);
				foreach (var sort in new[] { SortOption.Name, SortOption.DateModified, SortOption.DateCreated, SortOption.Size })
				foreach (var direction in new[] { SortDirection.Ascending, SortDirection.Descending })
				foreach (var alongside in new[] { false, true })
				foreach (var filesFirst in new[] { false, true })
				foreach (var showExtensions in new[] { false, true })
				{
					var actual = Win32ListingOrder.TryOrder(records, sort, direction, alongside, filesFirst, GroupOption.None,
						SortDirection.Ascending, GroupByDateUnit.Day, showExtensions, CancellationToken.None)!.Select(r => r.cFileName).ToList();
					var expected = Reference(records, sort, direction, alongside, filesFirst, showExtensions);
					CollectionAssert.AreEqual(expected, actual, $"count={count} sort={sort} direction={direction} alongside={alongside} filesFirst={filesFirst} extensions={showExtensions}");
				}
			}
		}

		[TestMethod]
		public void Grouped_comparison_matches_the_previous_LINQ_chain()
		{
			var random = new Random(7);
			var labels = new[] { (0, "Today"), (1, "Yesterday"), (2, "Earlier this week"), (5, "Last month") };
			var entries = Records(3000, 3).Select((record, index) =>
			{
				var (groupIndex, text) = labels[random.Next(labels.Length)];
				var isFile = ((FileAttributes)record.dwFileAttributes & FileAttributes.Directory) == 0;
				return new Win32ListingOrder.Entry(record, index, record.cFileName, isFile, Win32ListingOrder.FromFileTime(record.ftLastWriteTime),
					Win32ListingOrder.FromFileTime(record.ftCreationTime), isFile ? record.nFileSizeLow : 0, groupIndex, text);
			}).ToList();

			var natural = NaturalStringComparer.GetForProcessor();
			foreach (var groupDirection in new[] { SortDirection.Ascending, SortDirection.Descending })
			foreach (var direction in new[] { SortDirection.Ascending, SortDirection.Descending })
			foreach (var alongside in new[] { false, true })
			{
				IOrderedEnumerable<Win32ListingOrder.Entry> linq = groupDirection == SortDirection.Ascending
					? entries.OrderBy(x => x.GroupIndex).ThenBy(x => x.GroupText)
					: entries.OrderByDescending(x => x.GroupIndex).ThenByDescending(x => x.GroupText);
				if (!alongside)
					linq = linq.ThenBy(x => x.IsFile);
				linq = direction == SortDirection.Ascending ? linq.ThenBy(x => (object)x.Modified) : linq.ThenByDescending(x => (object)x.Modified);
				linq = direction == SortDirection.Ascending ? linq.ThenBy(x => x.Name, natural) : linq.ThenByDescending(x => x.Name, natural);

				var compare = Win32ListingOrder.CreateComparison(SortOption.DateModified, direction, alongside, false, GroupOption.DateModified, groupDirection);
				var sorted = entries.ToList();
				sorted.Sort(compare);
				CollectionAssert.AreEqual(linq.Select(x => x.Index).ToList(), sorted.Select(x => x.Index).ToList(),
					$"groupDirection={groupDirection} direction={direction} alongside={alongside}");
			}
		}

		[TestMethod]
		public void Records_named_by_the_shell_disable_the_early_order()
		{
			foreach (var special in new[] { "shortcut.lnk", "site.url", "Documents.library-ms" })
			{
				var records = Records(10, 1);
				records.Add(new() { cFileName = special, dwFileAttributes = (uint)FileAttributes.Archive });
				Assert.IsNull(Win32ListingOrder.TryOrder(records, SortOption.Name, SortDirection.Ascending, false, false, GroupOption.None,
					SortDirection.Ascending, GroupByDateUnit.Day, true, CancellationToken.None), special);
			}

			// desktop.ini can rename ReadOnly or System folders
			var withCustomFolder = Records(10, 1);
			withCustomFolder.Add(new() { cFileName = "Custom", dwFileAttributes = (uint)(FileAttributes.Directory | FileAttributes.ReadOnly) });
			Assert.IsNull(Win32ListingOrder.TryOrder(withCustomFolder, SortOption.Name, SortDirection.Ascending, false, false, GroupOption.None,
				SortDirection.Ascending, GroupByDateUnit.Day, true, CancellationToken.None));

			Assert.IsNull(Win32ListingOrder.TryOrder(Records(10, 1), SortOption.FileType, SortDirection.Ascending, false, false, GroupOption.None,
				SortDirection.Ascending, GroupByDateUnit.Day, true, CancellationToken.None));
		}

		[TestMethod]
		public void Ordered_prefix_yields_the_same_sequence_as_a_full_stable_sort()
		{
			var random = new Random(11);
			foreach (var count in new[] { 0, 1, 63, 64, 65, 500, 20_000 })
			{
				foreach (var shape in new[] { "random", "sorted", "reversed", "ties" })
				{
					var values = Enumerable.Range(0, count).Select(i => shape switch
					{
						"sorted" => i,
						"reversed" => count - i,
						"ties" => random.Next(5),
						_ => random.Next(),
					}).Select((value, index) => (Value: value, Index: index)).ToList();
					Comparison<(int Value, int Index)> compare = (x, y) => x.Value != y.Value ? x.Value.CompareTo(y.Value) : x.Index.CompareTo(y.Index);

					var expected = values.OrderBy(x => x.Value).ToList();
					var actual = OrderedPrefix.Read(values, compare, 64, CancellationToken.None).ToList();
					CollectionAssert.AreEqual(expected, actual, $"count={count} shape={shape}");
				}
			}
		}

		[TestMethod]
		public void Ordered_prefix_stops_when_cancelled()
		{
			using var cancellation = new CancellationTokenSource();
			var values = Enumerable.Range(0, 10_000).ToList();
			var sequence = OrderedPrefix.Read(values, (x, y) => x.CompareTo(y), 64, cancellation.Token);
			using var enumerator = sequence.GetEnumerator();
			Assert.IsTrue(enumerator.MoveNext());
			cancellation.Cancel();
			Assert.ThrowsExactly<OperationCanceledException>(() => { while (enumerator.MoveNext()) { } });
		}
	}
}
