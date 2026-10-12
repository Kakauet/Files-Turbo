// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	internal static class OrderedPrefix
	{
		/// <summary>
		/// Yields <paramref name="items"/> in the order of <paramref name="compare"/>, producing the first
		/// <paramref name="prefix"/> items before sorting the rest. The prefix costs about one comparison per item.
		/// </summary>
		/// <param name="compare">A strict total order: distinct items never compare equal.</param>
		internal static IEnumerable<T> Read<T>(IReadOnlyList<T> items, Comparison<T> compare, int prefix, CancellationToken token)
		{
			var count = Math.Min(prefix, items.Count);
			if (count == 0)
				yield break;

			// Max-heap of the smallest items seen so far
			var heap = new PriorityQueue<T, T>(count, Comparer<T>.Create((x, y) => compare(y, x)));
			for (var index = 0; index < items.Count; index++)
			{
				if ((index & 4095) == 0)
					token.ThrowIfCancellationRequested();
				var item = items[index];
				if (heap.Count < count)
					heap.Enqueue(item, item);
				else if (compare(item, heap.Peek()) < 0)
					heap.EnqueueDequeue(item, item);
			}

			var top = new T[count];
			for (var index = count - 1; index >= 0; index--)
				top[index] = heap.Dequeue();
			foreach (var item in top)
				yield return item;

			token.ThrowIfCancellationRequested();
			var last = top[^1];
			var rest = new List<T>(items.Count - count);
			foreach (var item in items)
			{
				if (compare(item, last) > 0)
					rest.Add(item);
			}
			rest.Sort(compare);
			for (var index = 0; index < rest.Count; index++)
			{
				if ((index & 4095) == 0)
					token.ThrowIfCancellationRequested();
				yield return rest[index];
			}
		}
	}
}
