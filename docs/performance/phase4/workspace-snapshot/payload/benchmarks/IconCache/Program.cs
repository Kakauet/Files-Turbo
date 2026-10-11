using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Files.App.Services;

namespace FilesTurbo.IconCacheBench;

internal static class Program
{
	private static readonly string[] Extensions = [".txt", ".png", ".json", ".cs", ".pdf", ".zip", ".mp4", ".xml"];
	private static long _consumed;

	public static async Task<int> Main(string[] args)
	{
		if (args.Length < 3 || args[0] is not ("benchmark" or "test"))
		{
			Console.Error.WriteLine("Usage: benchmark|test baseline|turbo absolute-output-directory [repetitions=31]");
			return 2;
		}

		var mode = args[0];
		var label = args[1];
		var outputDirectory = Path.GetFullPath(args[2]);
		Directory.CreateDirectory(outputDirectory);
		var metadata = new
		{
			variant = label,
			assembly = typeof(Program).Assembly.GetName().Name,
			runtime = RuntimeInformation.FrameworkDescription,
			os = RuntimeInformation.OSDescription,
			architecture = RuntimeInformation.ProcessArchitecture.ToString(),
			processor_count = Environment.ProcessorCount,
			server_gc = GCSettings.IsServerGC,
			tiered_compilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
			recorded_utc = DateTimeOffset.UtcNow,
			scope = "source-linked internal warm cache lookup; native shell and WinUI test doubles are excluded from timing"
		};
		File.WriteAllText(Path.Combine(outputDirectory, $"icon-cache-{label}-environment.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
		var tests = await ContractTests.RunAsync(label == "turbo");
		File.WriteAllText(Path.Combine(outputDirectory, $"icon-cache-{label}-tests.json"), JsonSerializer.Serialize(tests, new JsonSerializerOptions { WriteIndented = true }));
		Console.WriteLine($"{label}: {tests.Count(x => x.Status == "passed")} passed; {tests.Count(x => x.Status == "failed")} failed; {tests.Count(x => x.Status == "observed")} diagnostic observations");
		if (tests.Any(x => x.Status == "failed"))
			return 1;
		if (mode == "test")
			return 0;

		var repetitions = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 31;
		if (repetitions is < 5 or > 101)
			throw new ArgumentOutOfRangeException(nameof(repetitions));
		List<Sample> samples = [];
		foreach (var workload in new[] { "lowercase-extension", "uppercase-extension", "folder", "mixed" })
		{
			FileThumbnailHelper.Reset();
			var cache = new IconCacheService();
			var keys = MakeKeys(workload);
			foreach (var key in keys)
				await cache.GetIconAsync(null, key.Extension, key.IsFolder, key.Size);
			// Warm tiered JIT, dictionary lookup and completed-task paths before recording.
			for (var i = 0; i < 10; i++)
				Consume(cache, keys, 10000);
			var shellCallsBefore = FileThumbnailHelper.Calls;
			foreach (var operations in new[] { 1000, 10000, 50000, 100000 })
			{
				for (var repetition = 0; repetition < repetitions; repetition++)
				{
					GC.Collect();
					GC.WaitForPendingFinalizers();
					GC.Collect();
					var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
					var start = Stopwatch.GetTimestamp();
					Consume(cache, keys, operations);
					var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
					var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
					samples.Add(new(label, workload, operations, repetition + 1, elapsed, allocated));
				}
			}
			if (FileThumbnailHelper.Calls != shellCallsBefore)
				throw new InvalidOperationException("Measured warm-cache region unexpectedly invoked shell test double.");
		}

		File.WriteAllText(Path.Combine(outputDirectory, $"icon-cache-{label}-samples.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
		using var csv = new StreamWriter(Path.Combine(outputDirectory, $"icon-cache-{label}-samples.csv"));
		csv.WriteLine("variant,workload,operations,repetition,elapsed_ms,allocated_bytes");
		foreach (var sample in samples)
			csv.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{sample.Variant},{sample.Workload},{sample.Operations},{sample.Repetition},{sample.ElapsedMs:F6},{sample.AllocatedBytes}"));

		var summaries = samples.GroupBy(x => (x.Workload, x.Operations)).Select(g =>
		{
			var times = g.Select(x => x.ElapsedMs).Order().ToArray();
			var bytes = g.Select(x => x.AllocatedBytes).Order().ToArray();
			return new { variant = label, workload = g.Key.Workload, operations = g.Key.Operations, repetitions, median_ms = Percentile(times, 0.5), p95_ms = Percentile(times, 0.95), minimum_ms = times[0], maximum_ms = times[^1], median_allocated_bytes = bytes[bytes.Length / 2], cache = "warm", scope = "actual IconCacheService lookup; shell and WinUI are stubbed and excluded from timing" };
		}).ToArray();
		File.WriteAllText(Path.Combine(outputDirectory, $"icon-cache-{label}-summary.json"), JsonSerializer.Serialize(summaries, new JsonSerializerOptions { WriteIndented = true }));
		Console.WriteLine($"Recorded {samples.Count} warm-cache samples, {repetitions} per workload/operation count; consumed={_consumed}.");
		return 0;
	}

	private static void Consume(IconCacheService cache, Key[] keys, int count)
	{
		long total = 0;
		for (var i = 0; i < count; i++)
		{
			var key = keys[i % keys.Length];
			var bytes = cache.GetIconAsync(null, key.Extension, key.IsFolder, key.Size).GetAwaiter().GetResult();
			total += bytes?.Length ?? 0;
		}
		Interlocked.Add(ref _consumed, total);
	}

	private static Key[] MakeKeys(string workload) => workload switch
	{
		"lowercase-extension" => Extensions.Select(x => new Key(x, false, 32)).ToArray(),
		"uppercase-extension" => Extensions.Select(x => new Key(x.ToUpperInvariant(), false, 32)).ToArray(),
		"folder" => [new(null, true, 32)],
		"mixed" => [new(".txt", false, 16), new(".PNG", false, 32), new(null, false, 48), new("", false, 64), new(null, true, 16), new(".cs", true, 32), new(".pdf", false, 256), new(".JSON", false, 24)],
		_ => throw new ArgumentOutOfRangeException(nameof(workload))
	};

	private static double Percentile(double[] sorted, double percentile)
		=> sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1)];

	private readonly record struct Key(string? Extension, bool IsFolder, uint Size);
	private sealed record Sample(string Variant, string Workload, int Operations, int Repetition, double ElapsedMs, long AllocatedBytes);
}
