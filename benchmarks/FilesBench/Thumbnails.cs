using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace FilesBench;

/// <summary>
/// Thumbnails in a large-icon view. "warm" reopens a folder whose thumbnails every explorer has produced before;
/// "first-visit" opens a folder of freshly generated images that no thumbnail cache has seen.
/// The end point of a tile is the moment its image last changed before the whole view stayed still.
/// </summary>
internal static class Thumbnails
{
	private const int ImageCount = 48;

	public static int Run(BenchConfig config, string output)
	{
		var samplesPath = Path.Combine(output, "thumbnails.jsonl");
		var root = config.Folders[0];
		var warm = Path.Combine(root, "warm");
		if (!Directory.Exists(warm))
			Generate(warm, 1);

		var apps = config.Apps.Select(Program.CreateDriver).ToList();
		try
		{
			var tiles = new Dictionary<string, List<Rectangle>>();
			foreach (var app in apps)
			{
				app.Start(config.Empty);
				Program.WaitForIdle([app]);
				app.Place(config.Window);
				app.Navigate(warm, TimeSpan.FromSeconds(30));
				app.SetView("Grid");
				Program.ParkCursor(config.Window);
				var window = Native.WindowRect(app.Hwnd);
				Paint.WaitForStillness(window, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60));
				tiles[app.Name] = app.FindItemTiles(Program.DisplayNames(warm), Rectangle.FromLTRB(window.Left, window.Top + 120, window.Right, window.Bottom - 36));
				app.SaveWindow("thumbnails-calibration");
				Console.WriteLine($"{app.Name}: {tiles[app.Name].Count} visible tiles");
			}

			for (var repetition = 0; repetition < config.Repetitions; repetition++)
			{
				foreach (var kind in new[] { "warm", "first-visit" })
				{
					for (var position = 0; position < apps.Count; position++)
					{
						var app = apps[(position + repetition) % apps.Count];
						var folder = kind == "warm" ? warm : PrepareFirstVisit(app, config, Path.Combine(root, $"first-{app.Name}-{repetition}"), 1000 + repetition * 10 + position);
						var sample = Measure(app, apps, config, folder, kind, tiles[app.Name], repetition, position, output);
						File.AppendAllText(samplesPath, JsonSerializer.Serialize(sample, Program.Json) + Environment.NewLine);
						Console.WriteLine($"{repetition,2} {app.Name,-9} {kind,-11} first={sample.FirstMs,7:F1} ten={sample.TenMs,7:F1} all={sample.AllMs,7:F1} tiles={sample.Tiles} valid={sample.Valid} {sample.Reason}");
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

	private static ThumbnailSample Measure(AppDriver app, List<AppDriver> apps, BenchConfig config, string folder, string kind,
		List<Rectangle> tiles, int repetition, int position, string output)
	{
		try
		{
			Program.WaitForIdle(apps);
			app.Focus();
			app.Navigate(config.Empty, TimeSpan.FromSeconds(30));
			Program.WaitForIdle(apps);
			app.PrepareNavigation(folder);
			Program.ParkCursor(config.Window);
			Thread.Sleep(150);
			var process = app.Process;
			var cpu = process.TotalProcessorTime;
			var start = Stopwatch.GetTimestamp();
			app.Submit();
			var ownInput = Native.LastInputTick();
			var result = Settle.Observe(tiles, start, TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(60));
			var disturbed = Native.LastInputTick() != ownInput;
			process.Refresh();
			if (repetition == 0)
				app.SaveWindow($"thumbnails-{kind}");
			var reason = !result.Settled ? "tiles did not settle" : disturbed ? "user input during sample" : null;
			return new(app.Name, kind, repetition, position, result.First, result.FirstN(10), result.All, tiles.Count, reason is null, reason,
				(process.TotalProcessorTime - cpu).TotalMilliseconds, process.WorkingSet64 / 1048576.0, process.PrivateMemorySize64 / 1048576.0, DateTime.UtcNow);
		}
		catch (BenchmarkException ex)
		{
			return new(app.Name, kind, repetition, position, null, null, null, tiles.Count, false, ex.Message, 0, 0, 0, DateTime.UtcNow);
		}
	}

	/// <summary>
	/// Both explorers keep a view per folder: the new folder gets the icon view while still empty,
	/// and only then receives images no thumbnail cache has seen.
	/// </summary>
	private static string PrepareFirstVisit(AppDriver app, BenchConfig config, string folder, int seed)
	{
		Directory.CreateDirectory(folder);
		app.Focus();
		app.Navigate(folder, TimeSpan.FromSeconds(30));
		app.SetView("Grid");
		app.Navigate(config.Empty, TimeSpan.FromSeconds(30));
		return Generate(folder, seed);
	}

	/// <summary>Writes photo-sized JPEGs with content unique to <paramref name="seed"/>.</summary>
	public static string Generate(string folder, int seed)
	{
		Directory.CreateDirectory(folder);
		var random = new Random(seed);
		var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
		using var parameters = new EncoderParameters(1);
		parameters.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
		for (var i = 0; i < ImageCount; i++)
		{
			using var bitmap = new Bitmap(1600, 1200);
			using (var graphics = Graphics.FromImage(bitmap))
			{
				using var background = new LinearGradientBrush(new Rectangle(0, 0, 1600, 1200), RandomColor(random), RandomColor(random), random.Next(360));
				graphics.FillRectangle(background, 0, 0, 1600, 1200);
				for (var shape = 0; shape < 12; shape++)
				{
					using var brush = new SolidBrush(Color.FromArgb(180, RandomColor(random)));
					graphics.FillEllipse(brush, random.Next(-200, 1500), random.Next(-200, 1100), random.Next(100, 700), random.Next(100, 700));
				}
				using var font = new Font("Segoe UI", 120, FontStyle.Bold);
				graphics.DrawString($"{seed}-{i}", font, Brushes.White, 80, 900);
			}
			bitmap.Save(Path.Combine(folder, $"Foto {i + 1:D2}.jpg"), codec, parameters);
		}
		return folder;
	}

	private static Color RandomColor(Random random) => Color.FromArgb(random.Next(256), random.Next(256), random.Next(256));
}

internal sealed record ThumbnailSample(string App, string Kind, int Repetition, int Position, double? FirstMs, double? TenMs, double? AllMs, int Tiles,
	bool Valid, string? Reason, double CpuMs, double WorkingSetMiB, double PrivateMiB, DateTime Utc);
