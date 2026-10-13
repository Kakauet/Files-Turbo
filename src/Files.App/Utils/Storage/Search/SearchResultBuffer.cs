// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	// One drain owns each search's queue; a replaced search cannot consume its successor's results.
	internal sealed class SearchResultBuffer<T>
	{
		private readonly object sync = new();
		private readonly Queue<T> items = new();
		private bool draining, cancelled;

		internal bool Enqueue(IReadOnlyList<T> batch)
		{
			lock (sync)
			{
				if (cancelled || batch.Count == 0) return false;
				foreach (var item in batch) items.Enqueue(item);
				if (draining) return false;
				draining = true;
				return true;
			}
		}

		internal bool TryTake(int maximum, out List<T> batch)
		{
			lock (sync)
			{
				batch = new(Math.Min(maximum, items.Count));
				while (batch.Count < maximum && items.TryDequeue(out var item)) batch.Add(item);
				if (batch.Count > 0) return true;
				draining = false;
				return false;
			}
		}

		internal void Cancel()
		{
			lock (sync) { cancelled = true; items.Clear(); draining = false; }
		}
	}

	internal static class SearchResultPublication
	{
		internal static bool HasSamePrefix<T>(IList<T> current, IReadOnlyList<T> ordered) where T : class
		{
			if (current.Count == 0 || current.Count > ordered.Count) return false;
			for (var i = 0; i < current.Count; i++)
				if (!ReferenceEquals(current[i], ordered[i])) return false;
			return true;
		}
	}
}
