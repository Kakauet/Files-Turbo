using System.Text.Json;

namespace FilesBench;

/// <summary>
/// CPU and memory of each application while it shows a folder and nothing else happens.
/// CPU is the share of one core used by the application's process over the window.
/// Explorer's process is the desktop shell, so its figures include the taskbar and desktop.
/// </summary>
internal static class Idle
{
	private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

	public static int Run(BenchConfig config, string output)
	{
		var samplesPath = Path.Combine(output, "idle.jsonl");
		foreach (var spec in config.Apps)
		{
			using var app = Program.CreateDriver(spec);
			app.Start(config.Empty);
			app.Place(config.Window);
			app.SetView(config.View);
			Program.ParkCursor(config.Window);
			Record(app, "start, empty folder", samplesPath);
			foreach (var folder in config.Folders)
			{
				app.Focus();
				app.Navigate(folder, TimeSpan.FromSeconds(60));
				Program.ParkCursor(config.Window);
				Record(app, $"showing {Path.GetFileName(folder)}", samplesPath);
				app.Focus();
				app.Navigate(config.Empty, TimeSpan.FromSeconds(60));
				Program.ParkCursor(config.Window);
				Record(app, $"empty folder after {Path.GetFileName(folder)}", samplesPath);
			}
		}
		return 0;
	}

	private static void Record(AppDriver app, string phase, string samplesPath)
	{
		Thread.Sleep(Settle);
		var process = app.Process;
		var before = process.TotalProcessorTime;
		var watch = Stopwatch.StartNew();
		Thread.Sleep(Window);
		process.Refresh();
		var cpu = (process.TotalProcessorTime - before).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100;
		var sample = new
		{
			App = app.Name,
			Phase = phase,
			CpuPercentOfOneCore = Math.Round(cpu, 2),
			WorkingSetMiB = Math.Round(process.WorkingSet64 / 1048576.0, 1),
			PrivateMiB = Math.Round(process.PrivateMemorySize64 / 1048576.0, 1),
			process.HandleCount,
			Threads = process.Threads.Count,
			Utc = DateTime.UtcNow,
		};
		File.AppendAllText(samplesPath, JsonSerializer.Serialize(sample, Program.Json) + Environment.NewLine);
		Console.WriteLine($"{app.Name,-9} {phase,-34} cpu={sample.CpuPercentOfOneCore,6:F2}% ws={sample.WorkingSetMiB,7:F1} MiB private={sample.PrivateMiB,7:F1} MiB handles={sample.HandleCount}");
	}
}
