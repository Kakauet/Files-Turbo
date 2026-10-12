using System.Text.Json;

namespace FilesBench;

/// <summary>
/// Navigation from an empty folder to a target folder, typed into each application's address bar.
/// t0 is the Enter key press; the end points are first row, first ten rows and every visible row
/// painted in their settled form on the composed desktop.
/// </summary>
internal static class Navigation
{
	public static int Run(BenchConfig config, string output)
	{
		var samplesPath = Path.Combine(output, "navigation.jsonl");
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

			// Calibrate every application on every folder before measuring; this also warms each path once
			var calibrations = new Dictionary<(string App, string Folder), Calibration>();
			foreach (var folder in config.Folders)
				foreach (var app in apps)
				{
					app.Focus();
					calibrations[(app.Name, folder)] = Program.Calibrate(app, config, folder, output);
					Console.WriteLine($"calibrated {app.Name} {Path.GetFileName(folder)} rows={calibrations[(app.Name, folder)].Regions.Count} count={calibrations[(app.Name, folder)].CountArea}");
				}

			var random = new Random(config.Seed);
			var failures = 0;
			for (var repetition = 0; repetition < config.Repetitions; repetition++)
			{
				var folders = config.Folders.OrderBy(_ => random.Next()).ToList();
				foreach (var folder in folders)
				{
					// Rotate the application order every repetition so no application always runs first
					var order = Enumerable.Range(0, apps.Count).Select(i => apps[(i + repetition) % apps.Count]).ToList();
					for (var position = 0; position < order.Count; position++)
					{
						var app = order[position];
						NavigationSample sample;
						try
						{
							sample = Measure(app, apps, config, calibrations[(app.Name, folder)], repetition, position);
							failures = 0;
						}
						catch (BenchmarkException ex) when (++failures < 3)
						{
							sample = NavigationSample.Failed(app.Name, Path.GetFileName(folder), repetition, position, ex.Message);
						}
						File.AppendAllText(samplesPath, JsonSerializer.Serialize(sample, Program.Json) + Environment.NewLine);
						Console.WriteLine($"{repetition,2} {app.Name,-9} {Path.GetFileName(folder),-8} first={sample.FirstRowMs,7:F1} ten={sample.TenRowsMs,7:F1} all={sample.AllRowsMs,7:F1} listing={sample.ListingCompleteMs,8:F1} valid={sample.Valid} {sample.Reason}");
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

	private static NavigationSample Measure(AppDriver app, List<AppDriver> apps, BenchConfig config, Calibration calibration, int repetition, int position)
	{
		// Background work left by any application (e.g. finishing a 100k listing) must not overlap the sample
		var idleWaitMs = Program.WaitForIdle(apps);
		app.Focus();
		app.Navigate(config.Empty, TimeSpan.FromSeconds(30));
		idleWaitMs += Program.WaitForIdle(apps);
		Program.ParkCursor(config.Window);
		Paint.WaitForStillness(calibration.Area, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10));
		app.PrepareNavigation(calibration.Folder);
		Program.ParkCursor(config.Window);
		Thread.Sleep(150);

		var process = app.Process;
		var cpuBefore = process.TotalProcessorTime;
		var start = Stopwatch.GetTimestamp();
		app.Submit();
		var ownInput = Native.LastInputTick();
		var paint = Paint.Observe(calibration, start, TimeSpan.FromSeconds(30));
		var disturbed = Native.LastInputTick() != ownInput;
		process.Refresh();
		var reached = string.Equals(app.CurrentFolder()?.TrimEnd('\\'), calibration.Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

		var reason = !paint.Settled ? "rows did not settle" : !reached ? "wrong folder" : disturbed ? "user input during sample" : null;
		return new NavigationSample(app.Name, Path.GetFileName(calibration.Folder), repetition, position,
			paint.FirstRowMs, paint.TenRowsMs, paint.AllRowsMs, paint.ListingCompleteMs, calibration.Regions.Count, reason is null, reason,
			paint.Polls, paint.MaxPollGapMs, (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
			process.WorkingSet64 / 1048576.0, process.PrivateMemorySize64 / 1048576.0, idleWaitMs, DateTime.UtcNow);
	}
}

internal sealed record NavigationSample(string App, string Folder, int Repetition, int Position,
	double? FirstRowMs, double? TenRowsMs, double? AllRowsMs, double? ListingCompleteMs, int Rows, bool Valid, string? Reason,
	int Polls, double MaxPollGapMs, double CpuMs, double WorkingSetMiB, double PrivateMiB, double IdleWaitMs, DateTime Utc)
{
	public static NavigationSample Failed(string app, string folder, int repetition, int position, string reason)
		=> new(app, folder, repetition, position, null, null, null, null, 0, false, reason, 0, 0, 0, 0, 0, 0, DateTime.UtcNow);
}
