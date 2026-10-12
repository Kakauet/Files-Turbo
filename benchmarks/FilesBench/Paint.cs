using System.Drawing;

namespace FilesBench;

/// <summary>The final, settled appearance of a folder's first visible rows in one application.</summary>
internal sealed class Calibration
{
	public required string App { get; init; }
	public required string Folder { get; init; }
	public required Rectangle Area { get; init; }
	public required List<NameRegion> Regions { get; init; }
	public required byte[] Reference { get; init; }
	public Rectangle? CountArea { get; init; }
	public byte[]? CountReference { get; init; }
}

internal sealed record PaintResult(double? FirstRowMs, double? TenRowsMs, double? AllRowsMs, double? ListingCompleteMs, bool Settled, int Polls, double MaxPollGapMs, double[] FinalDifferences);

internal static class Paint
{
	// Mean absolute channel difference below which a name cell counts as identical to its settled image
	public const double MatchThreshold = 2.0;
	private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(400);

	public static Rectangle UnionOf(IEnumerable<NameRegion> regions)
	{
		var union = regions.Select(r => r.Bounds).Aggregate(Rectangle.Union);
		union.Inflate(2, 2);
		return union;
	}

	/// <summary>Waits until the area stops changing for <paramref name="quiet"/>.</summary>
	public static bool WaitForStillness(Rectangle area, TimeSpan quiet, TimeSpan timeout)
	{
		using var probe = new ScreenProbe(area);
		probe.Capture();
		var previous = probe.Snapshot();
		var stillSince = Stopwatch.GetTimestamp();
		var watch = Stopwatch.StartNew();
		while (watch.Elapsed < timeout)
		{
			Thread.Sleep(25);
			probe.Capture();
			if (!probe.Pixels.SequenceEqual(previous))
			{
				previous = probe.Snapshot();
				stillSince = Stopwatch.GetTimestamp();
			}
			else if (Stopwatch.GetElapsedTime(stillSince) >= quiet)
				return true;
		}
		return false;
	}

	/// <summary>
	/// Polls the screen from <paramref name="start"/> until every calibrated name cell shows its settled image and keeps it.
	/// A row counts from the last time it changed into its settled image, so a row painted at a temporary position
	/// and moved later is only counted once it is final.
	/// </summary>
	public static PaintResult Observe(Calibration calibration, long start, TimeSpan timeout)
	{
		using var probe = new ScreenProbe(calibration.Area);
		using var countProbe = calibration.CountArea is { } countArea ? new ScreenProbe(countArea) : null;
		long? countSince = null;
		var count = calibration.Regions.Count;
		var since = new long?[count];
		var differences = new double[count];
		var polls = 0;
		var last = start;
		double maxGap = 0;
		var settled = false;
		while (Stopwatch.GetElapsedTime(start) < timeout)
		{
			var now = probe.Capture();
			polls++;
			maxGap = Math.Max(maxGap, Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
			last = now;
			var all = true;
			for (var i = 0; i < count; i++)
			{
				differences[i] = probe.Difference(calibration.Reference, calibration.Regions[i].Bounds);
				if (differences[i] <= MatchThreshold)
					since[i] ??= now;
				else
				{
					since[i] = null;
					all = false;
				}
			}
			if (countProbe is not null)
			{
				countProbe.Capture();
				if (countProbe.Difference(calibration.CountReference!, calibration.CountArea!.Value) <= MatchThreshold)
					countSince ??= now;
				else
					countSince = null;
			}
			var countSettled = countProbe is null || countSince is { } c && Stopwatch.GetElapsedTime(c, now) >= SettleWindow;
			if (all && countSettled && Stopwatch.GetElapsedTime(since.Max()!.Value, now) >= SettleWindow)
			{
				settled = true;
				break;
			}
			Thread.Sleep(1);
		}

		double? Ms(long? t) => t is { } value ? Stopwatch.GetElapsedTime(start, value).TotalMilliseconds : null;
		double? MaxMs(IEnumerable<long?> values) => values.Any(v => v is null) ? null : Ms(values.Max());
		return new(Ms(since[0]), MaxMs(since.Take(Math.Min(10, count))), MaxMs(since), Ms(countSince), settled, polls, maxGap, differences.ToArray());
	}
}
