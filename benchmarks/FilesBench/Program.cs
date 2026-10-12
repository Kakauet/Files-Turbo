using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FilesBench;

internal sealed class AppSpec
{
	public required string Name { get; init; }
	public required string Kind { get; init; } // Files | Explorer
	public string? Family { get; init; }
	public string? Layout { get; init; }
}

internal sealed class BenchConfig
{
	public required List<AppSpec> Apps { get; init; }
	public required string Empty { get; init; }
	public List<string> Folders { get; init; } = [];
	public int Repetitions { get; init; } = 12;
	public string View { get; init; } = "Details";
	public Rectangle Window { get; init; } = new(20, 20, 1440, 900);
	public int Seed { get; init; } = 20261012;
}

internal static class Program
{
	public static string? Output { get; private set; }

	public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

	[STAThread]
	private static int Main(string[] args)
	{
		if (args.Length < 3)
		{
			Console.Error.WriteLine("FilesBench <inspect|nav|idle> <config.json> <output-directory>");
			return 2;
		}

		var config = JsonSerializer.Deserialize<BenchConfig>(File.ReadAllText(args[1]))!;
		var output = Output = Directory.CreateDirectory(args[2]).FullName;
		Native.timeBeginPeriod(1);
		try
		{
			return args[0] switch
			{
				"inspect" => Inspect(config, output),
				"nav" => Navigation.Run(config, output),
				"idle" => Idle.Run(config, output),
				_ => throw new ArgumentException($"Unknown command {args[0]}"),
			};
		}
		catch (BenchmarkException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 1;
		}
		finally
		{
			Native.timeEndPeriod(1);
		}
	}

	public static AppDriver CreateDriver(AppSpec spec) => spec.Kind switch
	{
		"Files" => new FilesDriver(spec.Name, spec.Family!, spec.Layout!),
		"Explorer" => new ExplorerDriver(),
		_ => throw new ArgumentException($"Unknown app kind {spec.Kind}"),
	};

	public static HashSet<string> DisplayNames(string folder)
	{
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var path in Directory.EnumerateFileSystemEntries(folder))
		{
			var name = Path.GetFileName(path);
			names.Add(name);
			// Explorer hides known extensions by default
			names.Add(Path.GetFileNameWithoutExtension(name));
		}
		return names;
	}

	/// <summary>
	/// Waits until every application process stays below 5% of one core for half a second.
	/// Explorer's process is the desktop shell, so it is held to the same rule but may wait longer.
	/// </summary>
	public static double WaitForIdle(IEnumerable<AppDriver> apps, int timeoutMs = 60_000)
	{
		var watch = Stopwatch.StartNew();
		var processes = apps.Select(app => app.Process).ToList();
		while (watch.ElapsedMilliseconds < timeoutMs)
		{
			var before = processes.Select(p => { p.Refresh(); return p.TotalProcessorTime; }).ToList();
			Thread.Sleep(500);
			var busiest = processes.Select((p, i) => { p.Refresh(); return (p.TotalProcessorTime - before[i]).TotalMilliseconds / 500; }).Max();
			if (busiest < 0.05)
				return watch.Elapsed.TotalMilliseconds;
		}
		return watch.Elapsed.TotalMilliseconds;
	}

	public static void ParkCursor(Rectangle window) => Native.SetCursorPos(window.Right + 40, window.Top + window.Height / 2);

	public static Calibration Calibrate(AppDriver app, BenchConfig config, string folder, string output)
	{
		app.Navigate(config.Empty, TimeSpan.FromSeconds(30));
		Thread.Sleep(500);
		app.Navigate(folder, TimeSpan.FromSeconds(60));
		ParkCursor(config.Window);
		var window = Native.WindowRect(app.Hwnd);
		var viewport = Rectangle.FromLTRB(window.Left, window.Top + 120, window.Right, window.Bottom - 36);
		Paint.WaitForStillness(viewport, TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(90));

		var regions = app.FindNameRegions(DisplayNames(folder), viewport);
		if (regions.Count < 10)
			throw new BenchmarkException($"{app.Name}: only {regions.Count} visible rows in {folder}.");
		var area = Paint.UnionOf(regions);
		using var probe = new ScreenProbe(area);
		probe.Capture();
		var countArea = app.FindItemCountRegion(Directory.EnumerateFileSystemEntries(folder).Count());
		byte[]? countReference = null;
		if (countArea is { } countRect)
		{
			using var countProbe = new ScreenProbe(countRect);
			countProbe.Capture();
			countReference = countProbe.Snapshot();
		}
		var calibration = new Calibration { App = app.Name, Folder = folder, Area = area, Regions = regions, Reference = probe.Snapshot(), CountArea = countArea, CountReference = countReference };

		// The reference must show text, not an empty or half-drawn list
		var blank = regions.Count(region => probe.Ink(region.Bounds) < 0.01);
		if (blank > 0)
			throw new BenchmarkException($"{app.Name}: {blank} calibrated name cells are blank in {folder}.");
		probe.Save(Path.Combine(output, $"calibration-{app.Name}-{Path.GetFileName(folder)}.png"));
		File.WriteAllText(Path.Combine(output, $"calibration-{app.Name}-{Path.GetFileName(folder)}.json"),
			JsonSerializer.Serialize(new { calibration.App, calibration.Folder, Area = area.ToString(), CountArea = countArea?.ToString(), Regions = regions.Select(r => new { r.Name, Bounds = r.Bounds.ToString() }) }, Json));
		return calibration;
	}

	private static int Inspect(BenchConfig config, string output)
	{
		foreach (var spec in config.Apps)
		{
			using var app = CreateDriver(spec);
			app.Start(config.Empty);
			app.Place(config.Window);
			app.SetView(config.View);
			foreach (var folder in config.Folders)
			{
				var calibration = Calibrate(app, config, folder, output);
				Console.WriteLine($"{app.Name} {Path.GetFileName(folder)}: {calibration.Regions.Count} rows, area {calibration.Area}");
				foreach (var region in calibration.Regions.Take(3))
					Console.WriteLine($"   {region.Name} {region.Bounds}");
			}
		}
		return 0;
	}
}
