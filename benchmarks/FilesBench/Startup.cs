using System.Drawing;
using System.Text.Json;

namespace FilesBench;

/// <summary>
/// Opening a window on a folder.
/// "new-process": Files is not running at all (LeaveAppRunning off) and is launched through its package.
/// "resident": the process is already running in the background (Files' default, LeaveAppRunning on)
/// or is the shell (Explorer), and only a new window is opened. Only "resident" compares the three explorers.
/// t0 is the launch request; the end points are the first window pixels and the settled first ten rows.
/// </summary>
internal static class Startup
{
	public static int Run(BenchConfig config, string output)
	{
		var samplesPath = Path.Combine(output, "startup.jsonl");
		var folder = config.Folders[0];
		var calibrations = new Dictionary<string, Calibration>();
		foreach (var spec in config.Apps)
		{
			// Calibrate at the benchmark geometry; Files restores this placement on its next start
			using var app = Program.CreateDriver(spec);
			if (app is FilesDriver files)
				files.Settings = new() { ["LeaveAppRunning"] = false };
			app.Start(folder);
			Program.WaitForIdle([app]);
			app.Place(config.Window);
			app.SetView(config.View);
			calibrations[spec.Name] = Program.Calibrate(app, config, folder, output);
			if (app is FilesDriver f)
			{
				// Leave the folder on screen so the next start shows the same first view, then close cleanly
				f.Navigate(folder, TimeSpan.FromSeconds(30));
				f.Close();
			}
		}

		for (var repetition = 0; repetition < config.Repetitions; repetition++)
		{
			for (var position = 0; position < config.Apps.Count; position++)
			{
				var spec = config.Apps[(position + repetition) % config.Apps.Count];
				foreach (var mode in spec.Kind == "Files" ? new[] { "new-process", "resident" } : new[] { "resident" })
				{
					var sample = Measure(spec, mode, folder, calibrations[spec.Name], config, repetition);
					File.AppendAllText(samplesPath, JsonSerializer.Serialize(sample, Program.Json) + Environment.NewLine);
					Console.WriteLine($"{repetition,2} {spec.Name,-9} {mode,-11} window={sample.WindowMs,7:F1} first={sample.FirstRowMs,7:F1} ten={sample.TenRowsMs,7:F1} valid={sample.Valid} {sample.Reason}");
				}
			}
		}
		return 0;
	}

	private static StartupSample Measure(AppSpec spec, string mode, string folder, Calibration calibration, BenchConfig config, int repetition)
	{
		using var app = Program.CreateDriver(spec);
		try
		{
			if (app is FilesDriver files)
			{
				files.Settings = new() { ["LeaveAppRunning"] = mode == "resident" };
				files.Close();
				if (mode == "resident")
				{
					// Start once, close the window: the process stays resident in the background
					files.Start(folder);
					Program.WaitForIdle([files]);
					files.CloseWindowOnly();
				}
			}
			Thread.Sleep(2000);
			Program.ParkCursor(config.Window);
			var lastInput = Native.LastInputTick();

			var start = Stopwatch.GetTimestamp();
			app.Launch(folder);
			var windowMs = app.WaitForWindow(start, TimeSpan.FromSeconds(30));
			if (Native.WindowRect(app.Hwnd) != config.Window)
				app.Place(config.Window);
			var paint = Paint.Observe(calibration, start, TimeSpan.FromSeconds(30));
			var disturbed = Native.LastInputTick() != lastInput;
			var geometry = Native.WindowRect(app.Hwnd) == config.Window;
			var reason = !paint.Settled ? "rows did not settle" : !geometry ? "window geometry differs" : disturbed ? "user input during sample" : null;
			return new(spec.Name, mode, repetition, windowMs, paint.FirstRowMs, paint.TenRowsMs, reason is null, reason, DateTime.UtcNow);
		}
		catch (BenchmarkException ex)
		{
			return new(spec.Name, mode, repetition, null, null, null, false, ex.Message, DateTime.UtcNow);
		}
	}
}

internal sealed record StartupSample(string App, string Mode, int Repetition, double? WindowMs, double? FirstRowMs, double? TenRowsMs, bool Valid, string? Reason, DateTime Utc);
