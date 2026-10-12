using System.Drawing;
using System.Runtime.InteropServices;

namespace FilesBench;

internal sealed record SettleResult(double?[] SettledMs, bool Settled, int Polls, double MaxPollGapMs)
{
	public double? First => SettledMs.Length > 0 ? SettledMs[0] : null;
	public double? FirstN(int n) => SettledMs.Length == 0 || SettledMs.Take(n).Any(v => v is null) ? null : SettledMs.Take(n).Max();
	public double? All => FirstN(SettledMs.Length);
}

/// <summary>
/// Reference-free variant of <see cref="Paint.Observe"/> for content whose final image is not known in advance
/// (thumbnails of files never seen before). Captures until every region has been unchanged for a quiet period and
/// reports, per region, when it last changed: the moment its final image appeared.
/// </summary>
internal static class Settle
{
	public static SettleResult Observe(IReadOnlyList<Rectangle> regions, long start, TimeSpan quiet, TimeSpan timeout)
	{
		var area = regions.Aggregate(Rectangle.Union);
		using var probe = new ScreenProbe(area);
		var hashes = new ulong[regions.Count];
		var changedAt = new long[regions.Count];
		var polls = 0;
		var last = start;
		double maxGap = 0;
		var lastChange = start;
		var settled = false;
		while (Stopwatch.GetElapsedTime(start) < timeout)
		{
			var now = probe.Capture();
			maxGap = Math.Max(maxGap, Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
			last = now;
			for (var i = 0; i < regions.Count; i++)
			{
				var hash = Hash(probe, regions[i]);
				if (polls == 0 || hash != hashes[i])
				{
					hashes[i] = hash;
					changedAt[i] = now;
					lastChange = now;
				}
			}
			polls++;
			if (Stopwatch.GetElapsedTime(lastChange, now) >= quiet)
			{
				settled = true;
				break;
			}
			Thread.Sleep(1);
		}

		// A region that never changed after the first capture was already final before the action
		var settledMs = changedAt.Select(t => settled ? (double?)Math.Max(0, Stopwatch.GetElapsedTime(start, t).TotalMilliseconds) : null).ToArray();
		return new(settledMs, settled, polls, maxGap);
	}

	private static ulong Hash(ScreenProbe probe, Rectangle region)
	{
		var pixels = probe.Pixels;
		var local = Rectangle.Intersect(new Rectangle(region.X - probe.Area.X, region.Y - probe.Area.Y, region.Width, region.Height), new Rectangle(Point.Empty, probe.Area.Size));
		var hash = 14695981039346656037UL;
		for (var y = local.Top; y < local.Bottom; y++)
		{
			var row = MemoryMarshal.Cast<byte, ulong>(pixels.Slice(y * probe.Stride + local.Left * 4, local.Width * 4 / 8 * 8));
			foreach (var value in row)
				hash = (hash ^ value) * 1099511628211UL;
		}
		return hash;
	}
}
