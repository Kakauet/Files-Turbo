using System.Diagnostics;
using System.Globalization;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text.Json;
using Files.App.Utils.Storage;
using Files.Shared.Helpers;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.FileProperties;
using FileAttributes = System.IO.FileAttributes;

internal static class Program
{
	private static readonly (string Name, string Query)[] Queries = [
		("exact", "file_000500_5.txt"), ("partial", "0500"), ("none", "does-not-exist"),
		("many", ".txt"), ("unicode", "東京"), ("wildcard", "file_0005??")];

	private readonly record struct Result(string Name, string Path, FileAttributes Attributes, long Length,
		DateTimeOffset Created, DateTimeOffset Modified);
	private sealed record Measurement(string Variant, int Process, int Count, string Scenario, int Iteration,
		double FirstMs, double TotalMs, int ResultCount, long AllocatedBytes, double CpuMs, long PeakWorkingSetBytes,
		long RetainedManagedBytes, string ResultHash);
	private sealed record CancellationMeasurement(string Variant, int Process, int Iteration, double ElapsedMs,
		bool CancellationObserved, int ResultCount, double NextSearchMs, int NextSearchCount);

	private static string Expression(string query)
	{
		if (query.Contains('.'))
		{
			var split = query.Split('.');
			query = $"{string.Join('.', split.SkipLast(1))}*.{split.Last()}";
		}
		return FileSystemName.TranslateWin32Expression($"*{query}*");
	}

	private static IEnumerable<Result> Legacy(string folder, string expression, CancellationToken token, bool optimized = false)
	{
		var folders = new Queue<string>();
		folders.Enqueue(folder);
		while (folders.TryDequeue(out var current))
		{
			using var handle = FindFirst(current, out var data);
			if (handle.IsInvalid) continue;
			do
			{
				token.ThrowIfCancellationRequested();
				var attributes = (FileAttributes)data.dwFileAttributes;
				var isDirectory = (attributes & FileAttributes.Directory) != 0;
				if (optimized && !isDirectory && !NativeMatches(ref data, expression))
					continue;
				var name = data.cFileName.ToString();
				if (name is "." or "..") continue;
				if ((attributes & FileAttributes.Directory) != 0 && ((attributes & FileAttributes.ReparsePoint) == 0 || (data.dwReserved0 & 0x20000000) == 0))
					folders.Enqueue(Path.Combine(current, name));
				if ((!optimized || isDirectory) && !FileSystemName.MatchesWin32Expression(expression, name)) continue;
				_ = !isDirectory && (optimized ? name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".url", StringComparison.OrdinalIgnoreCase) : FileExtensionHelpers.IsShortcutOrUrlFile(name));
				if ((attributes & FileAttributes.Hidden) != 0 || name.StartsWith('.')) continue;
				var created = DateTimeOffset.FromFileTime(((long)(uint)data.ftCreationTime.dwHighDateTime << 32) | (uint)data.ftCreationTime.dwLowDateTime);
				var modified = DateTimeOffset.FromFileTime(((long)(uint)data.ftLastWriteTime.dwHighDateTime << 32) | (uint)data.ftLastWriteTime.dwLowDateTime);
				yield return new(name, Path.Combine(current, name), attributes, ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow, created, modified);
			}
			while (!token.IsCancellationRequested && PInvoke.FindNextFile(handle, out data));
		}
	}

	private static unsafe Windows.Win32.FindCloseSafeHandle FindFirst(string folder, out WIN32_FIND_DATAW data)
	{
		WIN32_FIND_DATAW local = default;
		var handle = PInvoke.FindFirstFileEx(Path.Join(folder, "*"), FINDEX_INFO_LEVELS.FindExInfoBasic,
			&local, FINDEX_SEARCH_OPS.FindExSearchNameMatch, FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
		data = local;
		return handle;
	}

	private static IEnumerable<Result> Turbo(string folder, string expression, CancellationToken token)
	{
		var folders = new Queue<string>();
		folders.Enqueue(folder);
		while (folders.TryDequeue(out var current))
		{
			foreach (var entry in LocalSearchEnumerable.Enumerate(current, expression, false, false, false, false, token))
			{
				if ((entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
					folders.Enqueue(entry.Path);
				else if ((entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == (FileAttributes.Directory | FileAttributes.ReparsePoint))
				{
					using var reparseHandle = FindPath(entry.Path, out var data);
					if (!reparseHandle.IsInvalid && (data.dwReserved0 & 0x20000000) == 0)
						folders.Enqueue(entry.Path);
				}
				if (entry.IsMatch)
					yield return new(entry.Name, entry.Path, entry.Attributes, entry.Length, entry.Created, entry.Modified);
			}
		}
	}

	private static IEnumerable<Result> Search(string variant, string root, string query, CancellationToken token)
		=> variant == "phase2" ? Turbo(root, Expression(query), token) : Legacy(root, Expression(query), token, optimized: variant == "native");

	private static bool NativeMatches(ref WIN32_FIND_DATAW data, string expression)
	{
		var buffer = data.cFileName.AsSpan();
		var length = buffer.IndexOf('\0');
		return FileSystemName.MatchesWin32Expression(expression, length >= 0 ? buffer[..length] : buffer);
	}

	private static string Hash(IEnumerable<Result> results)
	{
		var paths = results.Select(x => x.Path).Order(StringComparer.OrdinalIgnoreCase);
		var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', paths));
		return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
	}

	public static async Task<int> Main(string[] args)
	{
		if (args.Length == 3 && args[0] == "--windows-search")
		{
			var probeRoot = Path.GetFullPath(args[1]);
			var probes = new List<object>();
			foreach (var count in new[] { 1000, 10000, 50000, 100000 })
			{
				try
				{
					var folder = await StorageFolder.GetFolderFromPathAsync(Path.Combine(probeRoot, count.ToString(CultureInfo.InvariantCulture)));
					var indexed = await folder.GetIndexedStateAsync();
					probes.Add(new { count, indexed_state = indexed.ToString() });
					if (count != 1000 && indexed != IndexedState.FullyIndexed)
						continue;
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
					var queryOptions = new QueryOptions { FolderDepth = FolderDepth.Deep, UserSearchFilter = "System.FileName:file_000500_5*.txt*", IndexerOption = IndexerOption.UseIndexerWhenAvailable };
					queryOptions.SortOrder.Clear();
					queryOptions.SortOrder.Add(new SortEntry { PropertyName = "System.Search.Rank", AscendingOrder = false });
					queryOptions.SetPropertyPrefetch(PropertyPrefetchOptions.BasicProperties, null);
					var queryResult = folder.CreateItemQueryWithOptions(queryOptions);
					var watch = Stopwatch.StartNew();
					var items = await queryResult.GetItemsAsync(0, 500).AsTask(cts.Token);
					probes.Add(new { count, exact_query_ms = watch.Elapsed.TotalMilliseconds, result_count = items.Count,
						scope_ok = items.All(x => x.Path.StartsWith(folder.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) });
				}
				catch (Exception ex) { probes.Add(new { count, error = ex.ToString() }); }
			}
			File.WriteAllText(args[2], JsonSerializer.Serialize(probes, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine("Read-only Windows Search probe written; existing indexed state only, no indexing settings changed.");
			return 0;
		}
		if (args.Length == 3 && args[0] == "--traversal-tests")
		{
			var fixtureRoot = Path.GetFullPath(args[1]);
			var legacy = Search("baseline", fixtureRoot, "", CancellationToken.None).ToList();
			var turbo = Search("phase2", fixtureRoot, "", CancellationToken.None).ToList();
			var contracts = new[]
			{
				new { name = "recursive legacy/phase2 result equivalence", passed = Hash(legacy) == Hash(turbo), detail = $"legacy={legacy.Count},phase2={turbo.Count}" },
				new { name = "junction scope/cycle not traversed", passed = !turbo.Any(x => x.Path.Contains("junction\\", StringComparison.OrdinalIgnoreCase)), detail = "Name-surrogate directory entries remain listed when matching, but their targets are not walked." },
				new { name = "recursive child exists", passed = turbo.Any(x => x.Name == "inside.txt"), detail = "Nonmatching directory names do not suppress recursion." },
			};
			File.WriteAllText(args[2], JsonSerializer.Serialize(contracts, new JsonSerializerOptions { WriteIndented = true }));
			Console.WriteLine($"Recursive search contracts: {contracts.Count(x => x.passed)} / {contracts.Length} passed.");
			return contracts.All(x => x.passed) ? 0 : 1;
		}
		if (args.Length < 4) return 2;
		var variant = args[0];
		var root = Path.GetFullPath(args[1]);
		var output = Path.GetFullPath(args[2]);
		var repetitions = int.Parse(args[3], CultureInfo.InvariantCulture);
		Directory.CreateDirectory(output);
		var rows = new List<Measurement>();
		using var process = Process.GetCurrentProcess();
		foreach (var count in new[] { 1000, 10000, 50000, 100000 })
		{
			var folder = Path.Combine(root, count.ToString(CultureInfo.InvariantCulture));
			foreach (var (name, query) in Queries)
			{
				_ = Search(variant, folder, query, CancellationToken.None).Count();
				for (var i = 0; i < repetitions; i++)
				{
					GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
					var allocated = GC.GetTotalAllocatedBytes(true);
					var cpu = process.TotalProcessorTime;
					var watch = Stopwatch.StartNew();
					var first = double.NaN;
					var results = new List<Result>();
					foreach (var item in Search(variant, folder, query, CancellationToken.None))
					{
						if (results.Count == 0) first = watch.Elapsed.TotalMilliseconds;
						results.Add(item);
					}
					var total = watch.Elapsed.TotalMilliseconds;
					var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
					var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
					process.Refresh();
					rows.Add(new(variant, Environment.ProcessId, count, name, i, first, total, results.Count,
						bytes, cpuMs, process.PeakWorkingSet64, GC.GetTotalMemory(false), Hash(results)));
				}
			}
		}
		var options = new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
		var cancellation = new List<CancellationMeasurement>();
		for (var i = 0; i < 5; i++)
		{
			using var cts = new CancellationTokenSource();
			cts.CancelAfter(10);
			var watch = Stopwatch.StartNew();
			var found = 0;
			var cancelled = false;
			try { foreach (var _ in Search(variant, Path.Combine(root, "100000"), "does-not-exist", cts.Token)) found++; }
			catch (OperationCanceledException) { cancelled = true; }
			cancelled |= cts.IsCancellationRequested;
			var elapsed = watch.Elapsed.TotalMilliseconds;
			watch.Restart();
			var nextCount = Search(variant, Path.Combine(root, "100000"), "file_000500_5.txt", CancellationToken.None).Count();
			cancellation.Add(new(variant, Environment.ProcessId, i, elapsed, cancelled, found, watch.Elapsed.TotalMilliseconds, nextCount));
		}
		File.WriteAllText(Path.Combine(output, $"{variant}-{Environment.ProcessId}-cancellation.json"), JsonSerializer.Serialize(cancellation, options));
		var sequenceWatch = Stopwatch.StartNew();
		var consecutiveCounts = Enumerable.Range(0, 12).Select(i => Search(variant, Path.Combine(root, "100000"), i % 2 == 0 ? "file_000500_5.txt" : "does-not-exist", CancellationToken.None).Count()).ToArray();
		File.WriteAllText(Path.Combine(output, $"{variant}-{Environment.ProcessId}-consecutive.json"), JsonSerializer.Serialize(new { variant, query_count = consecutiveCounts.Length, total_ms = sequenceWatch.Elapsed.TotalMilliseconds, result_counts = consecutiveCounts }, options));
		File.WriteAllText(Path.Combine(output, $"{variant}-{Environment.ProcessId}.json"), JsonSerializer.Serialize(rows, options));
		using var writer = File.CreateText(Path.Combine(output, $"{variant}-{Environment.ProcessId}.csv"));
		writer.WriteLine("variant,process,count,scenario,iteration,first_ms,total_ms,results,allocated_bytes,cpu_ms,peak_working_set_bytes,retained_managed_bytes,result_hash");
		foreach (var row in rows)
			writer.WriteLine(FormattableString.Invariant($"{row.Variant},{row.Process},{row.Count},{row.Scenario},{row.Iteration},{row.FirstMs:F4},{row.TotalMs:F4},{row.ResultCount},{row.AllocatedBytes},{row.CpuMs:F4},{row.PeakWorkingSetBytes},{row.RetainedManagedBytes},{row.ResultHash}"));
		Console.WriteLine($"{variant}: {rows.Count} measurements written; OS cache warm after scenario warmup; no UI or index measured.");
		return 0;
	}

	private static unsafe Windows.Win32.FindCloseSafeHandle FindPath(string path, out WIN32_FIND_DATAW data)
	{
		WIN32_FIND_DATAW local = default;
		var handle = PInvoke.FindFirstFileEx(path, FINDEX_INFO_LEVELS.FindExInfoBasic,
			&local, FINDEX_SEARCH_OPS.FindExSearchNameMatch, FIND_FIRST_EX_FLAGS.FIND_FIRST_EX_LARGE_FETCH);
		data = local;
		return handle;
	}
}
