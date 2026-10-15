// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class MetadataDateTests
	{
		[TestMethod]
		[DataRow("2026-01-31T23:30:00Z", 60)]
		[DataRow("2026-07-31T22:30:00Z", 120)]
		[DataRow("2026-10-25T00:30:00Z", 120)]
		[DataRow("2026-10-25T01:30:00Z", 60)]
		public void UtcMetadataFormatsAndGroupsLikeWinRt(string text, int offsetMinutes)
		{
			DateTime utc = DateTime.Parse(text, null, System.Globalization.DateTimeStyles.RoundtripKind);
			DateTimeOffset metadata = utc;
			DateTimeOffset winrt = metadata.ToOffset(TimeSpan.FromMinutes(offsetMinutes));
			foreach (IDateTimeFormatter formatter in new IDateTimeFormatter[] { new SystemDateTimeFormatter(), new UniversalDateTimeFormatter(), new ApplicationDateTimeFormatter() })
			{
				Assert.AreEqual(formatter.ToShortLabel(winrt), formatter.ToShortLabel(metadata));
				Assert.AreEqual(formatter.ToLongLabel(winrt), formatter.ToLongLabel(metadata));
				foreach (var unit in new[] { GroupByDateUnit.Day, GroupByDateUnit.Month, GroupByDateUnit.Year })
				{
					var expected = formatter.ToTimeSpanLabel(winrt, unit);
					var actual = formatter.ToTimeSpanLabel(metadata, unit);
					Assert.AreEqual(expected.Index, actual.Index);
					Assert.AreEqual(expected.Text, actual.Text);
				}
			}
		}

		[TestMethod]
		public void UtcMetadataPreservesChronologicalOrderAcrossRepeatedLocalHour()
		{
			var first = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2));
			var second = new DateTimeOffset(2026, 10, 25, 2, 15, 0, TimeSpan.FromHours(1));
			Assert.IsTrue(first < second);
			var before = new List<(string, bool, DateTimeOffset, DateTimeOffset, long)> { ("a", true, first, first, 1), ("b", true, second, second, 1) };
			var after = new List<(string, bool, DateTimeOffset, DateTimeOffset, long)> { ("a", true, first.UtcDateTime, first.UtcDateTime, 1), ("b", true, second.UtcDateTime, second.UtcDateTime, 1) };
			Assert.IsTrue(Win32ListingOrder.IsInOrder(before, SortOption.DateModified, SortDirection.Ascending, true, false, GroupOption.None, SortDirection.Ascending, GroupByDateUnit.Day));
			Assert.IsTrue(Win32ListingOrder.IsInOrder(after, SortOption.DateModified, SortDirection.Ascending, true, false, GroupOption.None, SortDirection.Ascending, GroupByDateUnit.Day));
		}
	}
}