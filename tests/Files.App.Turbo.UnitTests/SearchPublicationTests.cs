// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class SearchPublicationTests
	{
		[TestMethod]
		public void A_single_tick_can_be_drained_completely_in_bounded_batches()
		{
			var buffer = new SearchResultBuffer<int>();
			var expected = Enumerable.Range(0, 500).ToArray();
			Assert.IsTrue(buffer.Enqueue(expected));
			var found = new List<int>();
			while (buffer.TryTake(64, out var batch))
			{
				Assert.IsTrue(batch.Count <= 64);
				found.AddRange(batch);
			}
			CollectionAssert.AreEqual(expected, found);
		}

		[TestMethod]
		public void Producers_share_one_drain_and_an_empty_drain_can_restart()
		{
			var buffer = new SearchResultBuffer<int>();
			Assert.IsTrue(buffer.Enqueue([1, 2]));
			Assert.IsFalse(buffer.Enqueue([3]));
			Assert.IsTrue(buffer.TryTake(2, out var first));
			CollectionAssert.AreEqual(new[] { 1, 2 }, first);
			Assert.IsFalse(buffer.Enqueue([4]));
			Assert.IsTrue(buffer.TryTake(64, out var tail));
			CollectionAssert.AreEqual(new[] { 3, 4 }, tail);
			Assert.IsFalse(buffer.TryTake(64, out _));
			Assert.IsTrue(buffer.Enqueue([5]));
		}

		[TestMethod]
		public void Cancellation_discards_queued_and_late_items_without_affecting_a_new_search()
		{
			var old = new SearchResultBuffer<int>();
			var current = new SearchResultBuffer<int>();
			old.Enqueue([1, 2]);
			old.Cancel();
			Assert.IsFalse(old.Enqueue([3]));
			Assert.IsFalse(old.TryTake(64, out _));
			Assert.IsTrue(current.Enqueue([4]));
			Assert.IsTrue(current.TryTake(64, out var batch));
			CollectionAssert.AreEqual(new[] { 4 }, batch);
		}

		[TestMethod]
		public async Task Concurrent_producers_do_not_lose_or_duplicate_results()
		{
			var buffer = new SearchResultBuffer<int>();
			var drains = 0;
			await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
			{
				if (buffer.Enqueue(Enumerable.Range(worker * 500, 500).ToArray())) Interlocked.Increment(ref drains);
			})));
			Assert.AreEqual(1, drains);
			var found = new List<int>();
			while (buffer.TryTake(64, out var batch)) found.AddRange(batch);
			CollectionAssert.AreEquivalent(Enumerable.Range(0, 2000).ToArray(), found);
		}

		[TestMethod]
		public void Only_unchanged_rows_in_order_allow_tail_publication()
		{
			var a = new object(); var b = new object(); var c = new object();
			Assert.IsTrue(SearchResultPublication.HasSamePrefix(new[] { a, b }, new[] { a, b, c }));
			Assert.IsTrue(SearchResultPublication.HasSamePrefix(new[] { a, b }, new[] { a, b }));
			Assert.IsFalse(SearchResultPublication.HasSamePrefix(new[] { b, a }, new[] { a, b, c }));
			Assert.IsFalse(SearchResultPublication.HasSamePrefix(new[] { a, b, c }, new[] { a, b }));
			Assert.IsFalse(SearchResultPublication.HasSamePrefix(Array.Empty<object>(), new[] { a }));
			Assert.IsFalse(SearchResultPublication.HasSamePrefix(new[] { new object() }, new[] { a }));
		}

		[TestMethod]
		public async Task A_cancelled_suggestion_never_starts_or_publishes()
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			var called = false;
			await Assert.ThrowsAsync<OperationCanceledException>(() => new SearchSuggestionPipeline().RunAsync<int>("pro",
				() => { called = true; return Task.FromResult<IReadOnlyList<int>>([1]); }, () => true, _ => called = true,
				(_, _) => Task.CompletedTask, cancellation.Token));
			Assert.IsFalse(called);
		}

		[TestMethod]
		public async Task Superseded_suggestions_do_not_load_icons()
		{
			using var cancellation = new CancellationTokenSource();
			var enriched = false;
			await Assert.ThrowsAsync<OperationCanceledException>(() => new SearchSuggestionPipeline().RunAsync<int>("pro",
				() => Task.FromResult<IReadOnlyList<int>>([1]), () => true, _ => cancellation.Cancel(),
				(_, _) => { enriched = true; return Task.CompletedTask; }, cancellation.Token));
			Assert.IsFalse(enriched);
		}
	}
}
