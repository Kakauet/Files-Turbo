// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Media;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Files.App.Services
{
	public enum ThumbnailImageKind : byte
	{
		/// <summary>A thumbnail of the file's content.</summary>
		Thumbnail,

		/// <summary>An icon specific to the file (executables, shortcuts, custom folders).</summary>
		Icon,
	}

	/// <summary>
	/// Identifies a decoded image of one version of one item at one physical size.
	/// </summary>
	public readonly record struct ThumbnailImageKey(string Path, long ModifiedTicks, int PhysicalSize, ThumbnailImageKind Kind)
	{
		public static ThumbnailImageKey Create(string path, DateTimeOffset modified, int physicalSize, ThumbnailImageKind kind)
			=> new(path.ToUpperInvariant(), modified.UtcTicks, physicalSize, kind);
	}

	/// <summary>
	/// Least-recently-used images shared by every tab, bounded by an approximate byte budget.
	/// Images stay here after their rows are recycled, so returning to them needs no shell call.
	/// </summary>
	/// <remarks>Image access is limited to the UI thread that owns the images; the negative cache is thread safe.</remarks>
	public sealed class ThumbnailImageCache
	{
		public const long DefaultBudgetBytes = 150L * 1024 * 1024;
		private const int MaximumMissingEntries = 50_000;

		// A failed generation may be transient (locked file, busy extraction host), so it is retried after a while
		private static readonly TimeSpan MissingLifetime = TimeSpan.FromMinutes(2);

		private readonly Dictionary<ThumbnailImageKey, LinkedListNode<Entry>> entries = new();
		private readonly LinkedList<Entry> order = new();
		private readonly ConcurrentDictionary<(string Path, long ModifiedTicks), long> missing = new();

		public static ThumbnailImageCache Default { get; } = new(DefaultBudgetBytes);

		public ThumbnailImageCache(long budgetBytes)
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(budgetBytes, 1);
			BudgetBytes = budgetBytes;
		}

		public long BudgetBytes { get; }

		public long UsedBytes { get; private set; }

		public int Count => entries.Count;

		private sealed record Entry(ThumbnailImageKey Key, ImageSource Image, long Bytes);

		public bool TryGet(in ThumbnailImageKey key, [NotNullWhen(true)] out ImageSource? image)
		{
			if (entries.TryGetValue(key, out var node))
			{
				order.Remove(node);
				order.AddFirst(node);
				image = node.Value.Image;
				return true;
			}

			image = null;
			return false;
		}

		public void Add(in ThumbnailImageKey key, ImageSource image, long bytes)
		{
			bytes = Math.Max(1, bytes);
			if (entries.Remove(key, out var existing))
			{
				order.Remove(existing);
				UsedBytes -= existing.Value.Bytes;
			}

			// An image larger than the whole budget is still shown, just not retained
			if (bytes > BudgetBytes)
				return;

			entries[key] = order.AddFirst(new Entry(key, image, bytes));
			UsedBytes += bytes;
			while (UsedBytes > BudgetBytes && order.Last is { } oldest)
			{
				order.RemoveLast();
				entries.Remove(oldest.Value.Key);
				UsedBytes -= oldest.Value.Bytes;
			}
		}

		/// <summary>
		/// Forgets every image of an item, e.g. when its content changed without a new timestamp.
		/// </summary>
		public void Remove(string path)
		{
			var normalized = path.ToUpperInvariant();
			foreach (var key in entries.Keys.Where(key => key.Path == normalized).ToList())
			{
				if (entries.Remove(key, out var node))
				{
					order.Remove(node);
					UsedBytes -= node.Value.Bytes;
				}
			}
		}

		/// <summary>
		/// Keeps the images of a renamed item reachable under its new path.
		/// </summary>
		public void Rename(string oldPath, string newPath)
		{
			var normalized = oldPath.ToUpperInvariant();
			foreach (var key in entries.Keys.Where(key => key.Path == normalized).ToList())
			{
				if (entries.Remove(key, out var node))
				{
					order.Remove(node);
					UsedBytes -= node.Value.Bytes;
					Add(key with { Path = newPath.ToUpperInvariant() }, node.Value.Image, node.Value.Bytes);
				}
			}
		}

		public void Clear()
		{
			entries.Clear();
			order.Clear();
			UsedBytes = 0;
			missing.Clear();
		}

		/// <summary>
		/// Records that the shell produced no thumbnail for this version of the item.
		/// </summary>
		public void MarkMissing(string path, DateTimeOffset modified)
		{
			if (missing.Count >= MaximumMissingEntries)
				missing.Clear();
			missing[(path.ToUpperInvariant(), modified.UtcTicks)] = Environment.TickCount64;
		}

		public bool IsMissing(string path, DateTimeOffset modified)
			=> !missing.IsEmpty && missing.TryGetValue((path.ToUpperInvariant(), modified.UtcTicks), out var marked) &&
				Environment.TickCount64 - marked < MissingLifetime.TotalMilliseconds;
	}
}
