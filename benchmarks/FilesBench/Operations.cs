using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;

namespace FilesBench;

/// <summary>
/// File operations driven through each explorer's own commands on synthetic files only.
/// t0 is the key press that commits the operation (Ctrl+V, Enter on rename, Enter on the delete confirmation);
/// the end point is the file system reaching the final state. Integrity is checked with SHA-256 after the timer stops.
/// </summary>
internal static class Operations
{
	private const int SmallCount = 1000;
	private const int SmallBytes = 16 * 1024;
	private const long LargeBytes = 256L * 1024 * 1024;
	private const string Anchor = "ancla.txt";

	public static int Run(BenchConfig config, string output)
	{
		var samplesPath = Path.Combine(output, "operations.jsonl");
		var root = Directory.CreateDirectory(config.Folders[0]).FullName;
		var small = Path.Combine(root, "source-1000");
		var large = Path.Combine(root, "source-large");
		var manifest = PrepareSources(small, large);

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

			for (var repetition = 0; repetition < config.Repetitions; repetition++)
			{
				for (var position = 0; position < apps.Count; position++)
				{
					var app = apps[(position + repetition) % apps.Count];
					var work = Path.Combine(root, $"run-{app.Name}-{repetition}");
					foreach (var operation in new[] { "copy-1000", "copy-large", "move-1000", "rename", "delete-1000" })
					{
						var sample = Measure(app, apps, config, operation, small, large, work, manifest, repetition);
						File.AppendAllText(samplesPath, JsonSerializer.Serialize(sample, Program.Json) + Environment.NewLine);
						Console.WriteLine($"{repetition,2} {app.Name,-9} {operation,-12} total={sample.TotalMs,9:F1} verified={sample.Verified} valid={sample.Valid} {sample.Reason}");
					}
					DeleteOwned(work, root);
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

	private static Dictionary<string, string> PrepareSources(string small, string large)
	{
		var random = new Random(4242);
		if (!Directory.Exists(small))
		{
			Directory.CreateDirectory(small);
			var buffer = new byte[SmallBytes];
			for (var i = 0; i < SmallCount; i++)
			{
				random.NextBytes(buffer);
				File.WriteAllBytes(Path.Combine(small, $"dato {i + 1:D4}.bin"), buffer);
			}
		}
		if (!Directory.Exists(large))
		{
			Directory.CreateDirectory(large);
			using var stream = File.Create(Path.Combine(large, "grande.bin"));
			var buffer = new byte[4 * 1024 * 1024];
			for (long written = 0; written < LargeBytes; written += buffer.Length)
			{
				random.NextBytes(buffer);
				stream.Write(buffer);
			}
		}
		return Directory.EnumerateFiles(small).Concat(Directory.EnumerateFiles(large))
			.ToDictionary(path => Path.GetFileName(path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
	}

	private static OperationSample Measure(AppDriver app, List<AppDriver> apps, BenchConfig config, string operation,
		string small, string large, string work, Dictionary<string, string> manifest, int repetition)
	{
		var target = Path.Combine(work, operation);
		Directory.CreateDirectory(target);
		// Keyboard focus needs an item: paste targets hold a small anchor file, excluded from the checks
		var anchor = Path.Combine(target, Anchor);
		if (operation is "copy-1000" or "copy-large" or "move-1000")
			File.WriteAllText(anchor, "anchor");
		try
		{
			Program.WaitForIdle(apps);
			app.Focus();
			long start;
			Func<bool> done;
			Func<string?> verify;
			switch (operation)
			{
				case "copy-1000":
				case "copy-large":
				{
					var source = operation == "copy-1000" ? small : large;
					var names = Directory.EnumerateFiles(source).Select(Path.GetFileName).ToList();
					app.Navigate(source, TimeSpan.FromSeconds(30));
					Program.WaitForIdle(apps);
					app.SelectItem(names[0]!);
					app.Checkpoint($"{operation}-selected");
					app.AssertShowing(source);
					// Range selection from the first item: Explorer ignores an injected Ctrl+A
					Native.Chord(Native.VK_SHIFT, Native.VK_END);
					Thread.Sleep(300);
					app.Checkpoint($"{operation}-select-all");
					Native.Chord(Native.VK_CONTROL, 'C');
					Thread.Sleep(300);
					app.Navigate(target, TimeSpan.FromSeconds(30));
					Program.WaitForIdle(apps);
					app.SelectItem(Anchor);
					app.AssertShowing(target);
					start = Stopwatch.GetTimestamp();
					Native.Chord(Native.VK_CONTROL, 'V');
					done = () => Complete(target, names, source);
					verify = () => Verify(target, names, manifest);
					break;
				}
				case "move-1000":
				{
					// Move a private copy so the shared source stays intact
					var source = Path.Combine(work, "move-source");
					Directory.CreateDirectory(source);
					foreach (var file in Directory.EnumerateFiles(small))
						File.Copy(file, Path.Combine(source, Path.GetFileName(file)));
					var names = Directory.EnumerateFiles(source).Select(Path.GetFileName).ToList();
					app.Navigate(source, TimeSpan.FromSeconds(30));
					Program.WaitForIdle(apps);
					app.SelectItem(names[0]!);
					app.AssertShowing(source);
					// Range selection from the first item: Explorer ignores an injected Ctrl+A
					Native.Chord(Native.VK_SHIFT, Native.VK_END);
					Thread.Sleep(300);
					Native.Chord(Native.VK_CONTROL, 'X');
					Thread.Sleep(300);
					app.Navigate(target, TimeSpan.FromSeconds(30));
					Program.WaitForIdle(apps);
					app.SelectItem(Anchor);
					app.AssertShowing(target);
					start = Stopwatch.GetTimestamp();
					Native.Chord(Native.VK_CONTROL, 'V');
					done = () => !Directory.EnumerateFileSystemEntries(source).Any() && Complete(target, names, small);
					verify = () => Verify(target, names, manifest);
					break;
				}
				case "rename":
				{
					File.Copy(Path.Combine(small, "dato 0001.bin"), Path.Combine(target, "dato 0001.bin"));
					app.Navigate(target, TimeSpan.FromSeconds(30));
					Program.WaitForIdle(apps);
					app.SelectItem("dato 0001.bin");
					app.AssertShowing(target);
					Native.Chord(Native.VK_F2);
					var box = app.WaitForFocusedEdit();
					((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue("renombrado ñ 名前.bin");
					start = Stopwatch.GetTimestamp();
					Native.Chord(Native.VK_RETURN);
					done = () => File.Exists(Path.Combine(target, "renombrado ñ 名前.bin")) && !File.Exists(Path.Combine(target, "dato 0001.bin"));
					verify = () => Hash(Path.Combine(target, "renombrado ñ 名前.bin")) == manifest["dato 0001.bin"] ? null : "content changed";
					break;
				}
				default:
				{
					foreach (var file in Directory.EnumerateFiles(small))
						File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
					app.Navigate(target, TimeSpan.FromSeconds(30));
					Program.WaitForIdle(apps);
					app.SelectItem("dato 0001.bin");
					app.AssertShowing(target);
					// Range selection from the first item: Explorer ignores an injected Ctrl+A
					Native.Chord(Native.VK_SHIFT, Native.VK_END);
					Thread.Sleep(300);
					app.AssertShowing(target);
					Native.Chord(Native.VK_SHIFT, Native.VK_DELETE);
					var confirm = app.WaitForConfirmation();
					app.Checkpoint("delete-confirmation");
					start = Stopwatch.GetTimestamp();
					try
					{
						confirm.Invoke();
					}
					catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ElementNotAvailableException)
					{
						// WinUI may report an error after the dialog has closed; the file system decides below
					}
					done = () => !Directory.EnumerateFileSystemEntries(target).Any();
					verify = () => null;
					break;
				}
			}

			var ownInput = Native.LastInputTick();
			var process = app.Process;
			var cpu = process.TotalProcessorTime;
			var completed = false;
			while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromMinutes(3))
			{
				if (done())
				{
					completed = true;
					break;
				}
				Thread.Sleep(5);
			}
			var totalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
			process.Refresh();
			var disturbed = Native.LastInputTick() != ownInput;
			Program.WaitForIdle(apps);
			var integrity = completed ? verify() : null;
			var present = Directory.EnumerateFileSystemEntries(target).Count(path => Path.GetFileName(path) != Anchor);
			var reason = !completed ? $"operation did not complete ({present} entries in target)" : integrity is not null ? integrity : disturbed ? "user input during sample" : null;
			return new(app.Name, operation, repetition, completed ? totalMs : null, completed && integrity is null, reason is null, reason,
				(process.TotalProcessorTime - cpu).TotalMilliseconds, process.PrivateMemorySize64 / 1048576.0, DateTime.UtcNow);
		}
		catch (Exception ex) when (ex is BenchmarkException or InvalidOperationException or ElementNotAvailableException)
		{
			Native.Chord(Native.VK_ESCAPE);
			return new(app.Name, operation, repetition, null, false, false, ex.Message, 0, 0, DateTime.UtcNow);
		}
	}

	// Every file present with its final size and no writer left
	private static bool Complete(string target, List<string?> names, string source)
	{
		foreach (var name in names)
		{
			var path = Path.Combine(target, name!);
			var info = new FileInfo(path);
			if (!info.Exists || info.Length != new FileInfo(Path.Combine(source, name!)).Length)
				return false;
		}
		foreach (var name in names.TakeLast(3))
		{
			try { using var _ = File.Open(Path.Combine(target, name!), FileMode.Open, FileAccess.Read, FileShare.Read); }
			catch (IOException) { return false; }
		}
		return true;
	}

	private static string? Verify(string target, List<string?> names, Dictionary<string, string> manifest)
	{
		foreach (var name in names)
		{
			if (Hash(Path.Combine(target, name!)) != manifest[name!])
				return $"hash mismatch: {name}";
		}
		return Directory.EnumerateFiles(target).Count(path => Path.GetFileName(path) != Anchor) == names.Count ? null : "unexpected extra files";
	}

	private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

	// Only folders this benchmark created below its own root
	private static void DeleteOwned(string folder, string root)
	{
		var full = Path.GetFullPath(folder);
		if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("run-", StringComparison.Ordinal))
			throw new InvalidOperationException($"Refusing to delete {full}");
		if (Directory.Exists(full))
			Directory.Delete(full, recursive: true);
	}
}

internal sealed record OperationSample(string App, string Operation, int Repetition, double? TotalMs, bool Verified, bool Valid, string? Reason,
	double CpuMs, double PrivateMiB, DateTime Utc);
