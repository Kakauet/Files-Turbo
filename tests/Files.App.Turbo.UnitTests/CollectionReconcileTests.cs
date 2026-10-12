// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class CollectionReconcileTests
	{
		private static List<ListedItem> Items(int count, string prefix = "item")
			=> Enumerable.Range(0, count).Select(index => new ListedItem($"{prefix}{index:D5}") { Group = index % 2 == 0 ? "even" : "odd" }).ToList();

		private static BulkConcurrentObservableCollection<ListedItem> Collection(IEnumerable<ListedItem> items, bool grouped = false)
		{
			var collection = new BulkConcurrentObservableCollection<ListedItem>();
			if (grouped)
				collection.ItemGroupKeySelector = item => item.Group;
			collection.AddRange(items);
			if (grouped)
				collection.ResetGroups();
			return collection;
		}

		[TestMethod]
		public void Unchanged_snapshot_can_reconcile()
		{
			var items = Items(50);
			Assert.IsTrue(Collection(items).CanReconcile(items, 128));
		}

		[TestMethod]
		public void Few_insertions_and_removals_in_order_can_reconcile()
		{
			var items = Items(50);
			var next = items.ToList();
			next.RemoveAt(10);
			next.Insert(20, new ListedItem("new"));
			Assert.IsTrue(Collection(items).CanReconcile(next, 128));
		}

		[TestMethod]
		public void Resort_cannot_reconcile()
		{
			// A sort change keeps the same rows in another order; it must use one Reset, not n moves.
			var items = Items(2000);
			var reversed = Enumerable.Reverse(items).ToList();
			Assert.IsFalse(Collection(items, grouped: true).CanReconcile(reversed, 128));
		}

		[TestMethod]
		public void Single_swap_cannot_reconcile()
		{
			var items = Items(10);
			var next = items.ToList();
			(next[3], next[4]) = (next[4], next[3]);
			Assert.IsFalse(Collection(items).CanReconcile(next, 128));
		}

		[TestMethod]
		public void Too_many_changes_cannot_reconcile()
		{
			var items = Items(100);
			var next = items.Concat(Items(129, "extra")).ToList();
			Assert.IsFalse(Collection(items).CanReconcile(next, 128));
			Assert.IsTrue(Collection(items).CanReconcile(items.Concat(Items(128, "extra")).ToList(), 128));
		}

		[TestMethod]
		public void Retained_item_with_a_new_group_key_cannot_reconcile()
		{
			var items = Items(10);
			var collection = Collection(items, grouped: true);
			items[3].Group = "elsewhere";
			Assert.IsFalse(collection.CanReconcile(items, 128));
		}

		[TestMethod]
		public void Duplicates_cannot_reconcile()
		{
			var items = Items(5);
			Assert.IsFalse(Collection(items).CanReconcile([.. items, items[0]], 128));
		}

		[TestMethod]
		public void Reconcile_of_a_small_change_emits_only_adds_and_removes()
		{
			var items = Items(40);
			var collection = Collection(items, grouped: true);
			var next = items.ToList();
			var removed = next[7];
			next.RemoveAt(7);
			var added = new ListedItem("added") { Group = "even" };
			next.Insert(12, added);

			var events = new List<NotifyCollectionChangedEventArgs>();
			collection.CollectionChanged += (_, e) => events.Add(e);
			foreach (var group in collection.GroupedCollection!)
				group.CollectionChanged += (_, e) => events.Add(e);

			Assert.IsTrue(collection.CanReconcile(next, 128));
			collection.Reconcile(next);

			CollectionAssert.AreEqual(next, collection.ToList());
			Assert.IsFalse(events.Any(e => e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace));
			Assert.IsTrue(events.Count <= 6, $"Expected a handful of notifications, got {events.Count}.");

			var even = collection.GroupedCollection!.Single(group => group.Model.Key == "even");
			CollectionAssert.AreEqual(next.Where(item => item.Group == "even").ToList(), even.ToList());
			Assert.IsFalse(collection.GroupedCollection!.Any(group => group.Contains(removed)));
		}

		[TestMethod]
		public void Reconcile_removes_groups_that_become_empty()
		{
			var items = Items(4);
			var collection = Collection(items, grouped: true);
			var onlyEven = items.Where(item => item.Group == "even").ToList();

			Assert.IsTrue(collection.CanReconcile(onlyEven, 128));
			collection.Reconcile(onlyEven);

			Assert.AreEqual(1, collection.GroupedCollection!.Count);
			Assert.AreEqual("even", collection.GroupedCollection![0].Model.Key);
		}

		[TestMethod]
		public void AddRange_accepts_a_lazy_sequence_over_itself()
		{
			var collection = Collection(Items(3));
			collection.AddRange(collection.Where(item => item.Name.EndsWith('1')));
			Assert.AreEqual(4, collection.Count);
		}
	}
}
