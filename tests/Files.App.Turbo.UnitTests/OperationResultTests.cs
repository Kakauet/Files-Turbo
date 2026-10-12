// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class OperationResultTests
	{
		private sealed record Source(string Path, int Id);

		[TestMethod]
		public void Match_returns_sources_in_result_order_ignoring_case_and_skips_unknown_results()
		{
			var sources = new[] { new Source(@"C:\a\One.txt", 1), new Source(@"C:\a\two.txt", 2), new Source(@"C:\a\three.txt", 3) };
			var matched = OperationSourceMatch.Match([@"c:\A\TWO.txt", @"C:\a\missing.txt", @"C:\a\one.txt"], sources, s => s.Path);

			CollectionAssert.AreEqual(new[] { 2, 1 }, matched.Select(s => s.Id).ToArray());
		}

		[TestMethod]
		public void Match_uses_the_first_source_when_paths_repeat()
		{
			var sources = new[] { new Source(@"C:\a\x.txt", 1), new Source(@"C:\A\X.TXT", 2) };
			var matched = OperationSourceMatch.Match([@"C:\a\x.txt"], sources, s => s.Path);

			Assert.AreEqual(1, matched.Single().Id);
		}

		[TestMethod]
		public void Match_is_linear_for_large_operations()
		{
			var sources = Enumerable.Range(0, 100_000).Select(i => new Source($@"C:\big\file {i}.bin", i)).ToList();
			var watch = Stopwatch.StartNew();
			var matched = OperationSourceMatch.Match(sources.Select(s => s.Path.ToUpperInvariant()), sources, s => s.Path);

			Assert.AreEqual(100_000, matched.Count);
			Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(2), $"Matching took {watch.Elapsed}");
		}

		[TestMethod]
		public void RemoveAll_removes_every_match_in_one_pass_and_keeps_the_rest_in_order()
		{
			var collection = new ConcurrentCollection<string>(["a", "b", "c", "d", "b"]);
			var removed = collection.RemoveAll(x => x is "b" or "d");

			CollectionAssert.AreEqual(new[] { "b", "d", "b" }, removed);
			CollectionAssert.AreEqual(new[] { "a", "c" }, collection.ToList());
		}
	}
}
