using System.Drawing;
using System.IO.Enumeration;
using System.Text.Json;
using System.Windows.Automation;

namespace FilesBench;

/// <summary>
/// File name search started from each explorer's search box (Ctrl+F, query, Enter).
/// End points: first result rows and the final result count painted in their settled form.
/// The expected matches follow Files' rule (case-insensitive substring); the count each explorer shows is recorded.
/// </summary>
internal static class Search
{
	private sealed record Query(string Name, string Folder, string Text, bool Recursive);

	public static int Run(BenchConfig config, string output)
	{
		var samplesPath = Path.Combine(output, "search.jsonl");
		var synthetic = config.Folders[0];
		var queries = new List<Query>
		{
			new("exact", Path.Combine(synthetic, "10000"), "file_005000_6.txt", false),
			new("partial", Path.Combine(synthetic, "10000"), "東京", false),
			new("recursive", synthetic, "東京", true),
		};

		var apps = config.Apps.Select(Program.CreateDriver).ToList();
		try
		{
			foreach (var app in apps)
			{
				app.Start(config.Empty);
				Program.WaitForIdle([app]);
				app.Place(config.Window);
				app.SetView(config.View);
			}

			var calibrations = new Dictionary<(string, string), (Calibration? Calibration, int? Count)>();
			foreach (var query in queries)
			{
				var expected = Expected(query);
				foreach (var app in apps)
				{
					calibrations[(app.Name, query.Name)] = Calibrate(app, apps, config, query, expected, output);
					var (calibration, count) = calibrations[(app.Name, query.Name)];
					Console.WriteLine($"calibrated {app.Name} {query.Name}: expected={expected.Count} shown={count} rows={calibration?.Regions.Count}");
					File.AppendAllText(Path.Combine(output, "search-calibration.jsonl"), JsonSerializer.Serialize(new { App = app.Name, Query = query.Name, query.Text, Expected = expected.Count, Shown = count, Rows = calibration?.Regions.Count }, Program.Json) + Environment.NewLine);
				}
			}

			for (var repetition = 0; repetition < config.Repetitions; repetition++)
			{
				foreach (var query in queries)
				{
					for (var position = 0; position < apps.Count; position++)
					{
						var app = apps[(position + repetition) % apps.Count];
						var (calibration, _) = calibrations[(app.Name, query.Name)];
						if (calibration is null)
							continue;
						var sample = Measure(app, apps, config, query, calibration, repetition, position);
						File.AppendAllText(samplesPath, JsonSerializer.Serialize(sample, Program.Json) + Environment.NewLine);
						Console.WriteLine($"{repetition,2} {app.Name,-9} {query.Name,-9} first={sample.FirstRowMs,7:F1} ten={sample.TenRowsMs,7:F1} complete={sample.CompleteMs,8:F1} valid={sample.Valid} {sample.Reason}");
					}
				}
			}
			return 0;
		}
		finally
		{
			foreach (var app in apps)
				app.Dispose();
		}
	}

	private static List<string> Expected(Query query)
	{
		var expression = FileSystemName.TranslateWin32Expression($"*{query.Text}*");
		return Directory.EnumerateFileSystemEntries(query.Folder, "*", query.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
			.Select(Path.GetFileName).Where(name => FileSystemName.MatchesWin32Expression(expression, name!)).Select(name => name!).ToList();
	}

	private static void StartSearch(AppDriver app, Query query)
	{
		app.Focus();
		var box = app.FocusSearchBox();
		((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(query.Text);
	}

	private static (Calibration?, int?) Calibrate(AppDriver app, List<AppDriver> apps, BenchConfig config, Query query, List<string> expected, string output)
	{
		Program.WaitForIdle(apps);
		app.Focus();
		app.Navigate(query.Folder, TimeSpan.FromSeconds(60));
		StartSearch(app, query);
		app.Submit();
		Program.ParkCursor(config.Window);
		var window = Native.WindowRect(app.Hwnd);
		var viewport = Rectangle.FromLTRB(window.Left, window.Top + 120, window.Right, window.Bottom - 36);
		Thread.Sleep(1000);
		Paint.WaitForStillness(viewport, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(180));
		Program.WaitForIdle(apps, 180_000);
		app.SaveWindow($"search-{query.Name}");

		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var name in expected)
		{
			names.Add(name);
			names.Add(Path.GetFileNameWithoutExtension(name));
		}
		var regions = app.FindNameRegions(names, viewport).Take(10).ToList();
		var (countArea, shown) = app.FindAnyItemCountRegion();
		if (regions.Count == 0 || countArea is null)
			return (null, shown);
		var area = Paint.UnionOf(regions);
		using var probe = new ScreenProbe(area);
		probe.Capture();
		using var countProbe = new ScreenProbe(countArea.Value);
		countProbe.Capture();
		return (new Calibration { App = app.Name, Folder = query.Folder, Area = area, Regions = regions, Reference = probe.Snapshot(), CountArea = countArea, CountReference = countProbe.Snapshot() }, shown);
	}

	private static SearchSample Measure(AppDriver app, List<AppDriver> apps, BenchConfig config, Query query, Calibration calibration, int repetition, int position)
	{
		try
		{
			Program.WaitForIdle(apps);
			app.Focus();
			app.Navigate(query.Folder, TimeSpan.FromSeconds(60));
			Program.WaitForIdle(apps);
			StartSearch(app, query);
			Program.ParkCursor(config.Window);
			var process = app.Process;
			var cpu = process.TotalProcessorTime;
			var start = Stopwatch.GetTimestamp();
			app.Submit();
			var ownInput = Native.LastInputTick();
			var paint = Paint.Observe(calibration, start, TimeSpan.FromSeconds(180));
			var disturbed = Native.LastInputTick() != ownInput;
			process.Refresh();
			var reason = !paint.Settled ? "results did not settle" : disturbed ? "user input during sample" : null;
			return new(app.Name, query.Name, query.Text, repetition, position, paint.FirstRowMs, paint.TenRowsMs, paint.ListingCompleteMs, reason is null, reason,
				(process.TotalProcessorTime - cpu).TotalMilliseconds, process.WorkingSet64 / 1048576.0, process.PrivateMemorySize64 / 1048576.0, DateTime.UtcNow);
		}
		catch (BenchmarkException ex)
		{
			return new(app.Name, query.Name, query.Text, repetition, position, null, null, null, false, ex.Message, 0, 0, 0, DateTime.UtcNow);
		}
	}
}

internal sealed record SearchSample(string App, string Query, string Text, int Repetition, int Position, double? FirstRowMs, double? TenRowsMs, double? CompleteMs,
	bool Valid, string? Reason, double CpuMs, double WorkingSetMiB, double PrivateMiB, DateTime Utc);
