using System.Diagnostics;
using System.Globalization;
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Files.App.Utils.Storage;
using Files.Shared.Helpers;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

internal static class Program
{
	private sealed record Contract(string Name, bool Passed, string Detail);
	private sealed record Sample(string Variant, int Count, string Scenario, int Iteration,
		double PublishedMs, double ProviderMs, int ResultCount, string ResultHash);
	private static readonly (string Scenario, string Query)[] Queries = [
		("exact", "file_000500_5.txt"), ("partial", "0500"), ("none", "does-not-exist"),
		("many", ".txt"), ("unicode", "東京"), ("wildcard", "file_0005??")];

	private static void Assert(bool value, string detail)
	{
		if (!value) throw new InvalidOperationException(detail);
	}

	private static async Task ExpectCancellation(Task operation)
	{
		try { await operation; }
		catch (OperationCanceledException) { return; }
		throw new InvalidOperationException("Expected cancellation.");
	}

	private static async Task<int> Contracts(string output)
	{
		List<Contract> contracts = [];
		async Task Test(string name, Func<Task> test)
		{
			try
			{
				await test().WaitAsync(TimeSpan.FromSeconds(8));
				contracts.Add(new(name, true, ""));
			}
			catch (Exception ex) { contracts.Add(new(name, false, ex.ToString())); }
		}
		await Test("names publish while icon enrichment is still blocked", async () =>
		{
			var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var icons = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var operation = new SearchSuggestionPipeline().RunAsync("file",
				() => Task.FromResult<IReadOnlyList<string>>(["file.txt"]), () => true,
				results => { Assert(results.Single() == "file.txt", "Wrong name"); published.SetResult(); },
				(_, token) => icons.Task.WaitAsync(token), default);
			await published.Task;
			Assert(!operation.IsCompleted, "Publication waited for icons");
			icons.SetResult();
			await operation;
		});
		await Test("cancel during debounce skips provider and publication", async () =>
		{
			using var cts = new CancellationTokenSource();
			var searched = false;
			var published = false;
			var operation = new SearchSuggestionPipeline().RunAsync("file",
				() => { searched = true; return Task.FromResult<IReadOnlyList<string>>([]); }, () => true,
				_ => published = true, (_, _) => Task.CompletedTask, cts.Token);
			cts.Cancel();
			await ExpectCancellation(operation);
			Assert(!searched && !published, "Canceled request ran");
		});
		await Test("old noncooperative provider cannot publish after cancellation", async () =>
		{
			using var cts = new CancellationTokenSource();
			var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var provider = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
			var published = false;
			var enriched = false;
			var operation = new SearchSuggestionPipeline().RunAsync("file",
				() => { entered.SetResult(); return provider.Task; }, () => true,
				_ => published = true, (_, _) => { enriched = true; return Task.CompletedTask; }, cts.Token);
			await entered.Task;
			cts.Cancel();
			provider.SetResult(["stale.txt"]);
			await ExpectCancellation(operation);
			Assert(!published && !enriched, "Stale request published or enriched");
		});
		await Test("changed scope during debounce skips filesystem work", async () =>
		{
			var current = false;
			var searched = false;
			await new SearchSuggestionPipeline().RunAsync("file",
				() => { searched = true; return Task.FromResult<IReadOnlyList<string>>([]); }, () => current,
				_ => throw new InvalidOperationException("Stale publication"), (_, _) => Task.CompletedTask, default);
			Assert(!searched, "Old scope searched");
		});
		await Test("changed scope after provider rejects old results", async () =>
		{
			var current = true;
			var published = false;
			await new SearchSuggestionPipeline().RunAsync("file",
				() => { current = false; return Task.FromResult<IReadOnlyList<string>>(["old-scope"]); }, () => current,
				_ => published = true, (_, _) => throw new InvalidOperationException("Stale enrichment"), default);
			Assert(!published, "Old scope published");
		});
		await Test("scope change during publication skips icon work", async () =>
		{
			var current = true;
			var enriched = false;
			await new SearchSuggestionPipeline().RunAsync("file",
				() => Task.FromResult<IReadOnlyList<string>>(["file"]), () => current,
				_ => current = false, (_, _) => { enriched = true; return Task.CompletedTask; }, default);
			Assert(!enriched, "Old icons queued");
		});
		await Test("canceled visible request stops asynchronous enrichment", async () =>
		{
			using var cts = new CancellationTokenSource();
			var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var operation = new SearchSuggestionPipeline().RunAsync("file",
				() => Task.FromResult<IReadOnlyList<string>>(["file"]), () => true, _ => { },
				(_, token) => { entered.SetResult(); return never.Task.WaitAsync(token); }, cts.Token);
			await entered.Task;
			cts.Cancel();
			await ExpectCancellation(operation);
		});
		await Test("rapid typing only runs and publishes the final query", async () =>
		{
			var pipeline = new SearchSuggestionPipeline();
			List<CancellationTokenSource> sources = [];
			List<Task> requests = [];
			List<string> queries = [];
			List<string> publications = [];
			var generation = 0;
			foreach (var query in new[] { "f", "fi", "fil", "file", "file_", "file_0" })
			{
				if (sources.Count > 0) sources[^1].Cancel();
				var cts = new CancellationTokenSource();
				sources.Add(cts);
				var ownGeneration = ++generation;
				requests.Add(Run());
				async Task Run()
				{
					try
					{
						await pipeline.RunAsync(query,
							() => { queries.Add(query); return Task.FromResult<IReadOnlyList<string>>([query]); },
							() => ownGeneration == generation, found => publications.Add(found.Single()),
							(_, _) => Task.CompletedTask, cts.Token);
					}
					catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
				}
				await Task.Delay(15);
			}
			await Task.WhenAll(requests);
			foreach (var cts in sources) cts.Dispose();
			Assert(queries.SequenceEqual(new[] { "file_0" }) && publications.SequenceEqual(queries), "Intermediate query ran/published");
		});
		await Test("independent tabs keep independent results under concurrency", async () =>
		{
			var operations = Enumerable.Range(0, 12).Select(async index =>
			{
				var value = "東京_" + index;
				var published = "";
				await new SearchSuggestionPipeline().RunAsync(value,
					() => Task.FromResult<IReadOnlyList<string>>([value]), () => true,
					found => published = found.Single(), (_, _) => Task.CompletedTask, default);
				Assert(published == value, "Tab results crossed");
			});
			await Task.WhenAll(operations);
		});
		await Test("empty results are published without fabricated entries", async () =>
		{
			var count = -1;
			await new SearchSuggestionPipeline().RunAsync("no-such-file",
				() => Task.FromResult<IReadOnlyList<string>>([]), () => true,
				found => count = found.Count, (_, _) => Task.CompletedTask, default);
			Assert(count == 0, "Missing or fabricated empty result");
		});
		await Test("slow provider restores debounce for the next query", async () =>
		{
			var pipeline = new SearchSuggestionPipeline();
			await pipeline.RunAsync("file", async () =>
			{
				await Task.Delay(180);
				return (IReadOnlyList<string>)["file"];
			}, () => true, _ => { }, (_, _) => Task.CompletedTask, default);
			var started = Stopwatch.GetTimestamp();
			await pipeline.RunAsync("files", () =>
			{
				Assert(Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 180, "Slow provider did not back off");
				return Task.FromResult<IReadOnlyList<string>>([]);
			}, () => true, _ => { }, (_, _) => Task.CompletedTask, default);
		});
		Directory.CreateDirectory(output);
		await File.WriteAllTextAsync(Path.Combine(output, "search-pipeline-contracts.json"), JsonSerializer.Serialize(contracts, new JsonSerializerOptions { WriteIndented = true }));
		Console.WriteLine($"{contracts.Count(x => x.Passed)}/{contracts.Count} search pipeline contracts passed.");
		return contracts.All(x => x.Passed) ? 0 : 1;
	}

	private static string Expression(string query)
	{
		if (query.Contains('.'))
		{
			var split = query.Split('.');
			query = $"{string.Join('.', split.SkipLast(1))}*.{split.Last()}";
		}
		return FileSystemName.TranslateWin32Expression($"*{query}*");
	}

	private static IEnumerable<string> Native(string root, string expression)
	{
		var folders = new Queue<string>();
		folders.Enqueue(root);
		while (folders.TryDequeue(out var folder))
		{
			foreach (var entry in NativeNameSearch.Enumerate(folder, expression, false, false, false, false, default))
			{
				if ((entry.Attributes & FileAttributes.Directory) != 0 && (entry.ReparseTag & 0x20000000) == 0)
					folders.Enqueue(entry.Path);
				if (entry.IsMatch) yield return entry.Path;
			}
		}
	}

	private static IEnumerable<string> Original(string root, string expression)
	{
		var folders = new Queue<string>();
		folders.Enqueue(root);
		while (folders.TryDequeue(out var folder))
		{
			using var handle = FindFirst(folder, out var data);
			if (handle.IsInvalid) continue;
			do
			{
				var name = data.cFileName.ToString();
				if (name is "." or "..") continue;
				var attributes = (FileAttributes)data.dwFileAttributes;
				var directory = (attributes & FileAttributes.Directory) != 0;
				if (directory && (data.dwReserved0 & 0x20000000) == 0) folders.Enqueue(Path.Combine(folder, name));
				if (!FileSystemName.MatchesWin32Expression(expression, name)) continue;
				_ = !directory && FileExtensionHelpers.IsShortcutOrUrlFile(name);
				if ((attributes & FileAttributes.Hidden) == 0 && !name.StartsWith('.')) yield return Path.Combine(folder, name);
			}
			while (PInvoke.FindNextFile(handle, out data));
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

	private static async Task Benchmark(string synthetic, string output, int repetitions)
	{
		if (Directory.Exists(output)) throw new InvalidOperationException("Choose a fresh results directory.");
		Directory.CreateDirectory(output);
		List<Sample> samples = [];
		foreach (var variant in new[] { "Original", "PhaseII", "PhaseIII", "PhaseIII", "PhaseII", "Original" })
		{
			foreach (var count in new[] { 1000, 10000, 50000, 100000 })
			{
				var root = Path.Combine(synthetic, count.ToString(CultureInfo.InvariantCulture));
				if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
				foreach (var (scenario, query) in Queries)
				{
					for (var iteration = -1; iteration < repetitions; iteration++)
					{
						var expression = Expression(query);
						IReadOnlyList<string> results = [];
						var providerMs = 0d;
						var publishedMs = 0d;
						var started = Stopwatch.GetTimestamp();
						async Task<IReadOnlyList<string>> Search()
						{
							var providerStarted = Stopwatch.GetTimestamp();
							results = await Task.Run(() => (variant == "Original" ? Original(root, expression) : Native(root, expression)).Take(10).ToArray());
							providerMs = Stopwatch.GetElapsedTime(providerStarted).TotalMilliseconds;
							return results;
						}
						if (variant == "PhaseIII")
						{
							await new SearchSuggestionPipeline().RunAsync(query, Search, () => true,
								_ => publishedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
								(_, _) => Task.CompletedTask, default);
						}
						else
						{
							await Task.Delay(200);
							await Search();
							publishedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
						}
						if (iteration < 0) continue;
						var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', results.Order(StringComparer.Ordinal)))));
						samples.Add(new(variant, count, scenario, iteration, publishedMs, providerMs, results.Count, hash));
					}
				}
			}
			Console.WriteLine($"Measured {variant}.");
		}
		Assert(samples.GroupBy(x => (x.Count, x.Scenario)).All(group => group.Select(x => x.ResultHash).Distinct().Count() == 1), "Suggestion name result set mismatch");
		await File.WriteAllTextAsync(Path.Combine(output, "pipeline-publication.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
		using var csv = new StreamWriter(Path.Combine(output, "pipeline-publication.csv"));
		await csv.WriteLineAsync("variant,count,scenario,iteration,published_ms,provider_ms,result_count,result_hash");
		foreach (var row in samples)
			await csv.WriteLineAsync(FormattableString.Invariant($"{row.Variant},{row.Count},{row.Scenario},{row.Iteration},{row.PublishedMs:F3},{row.ProviderMs:F3},{row.ResultCount},{row.ResultHash}"));
		using var summary = new StreamWriter(Path.Combine(output, "summary.csv"));
		await summary.WriteLineAsync("variant,count,scenario,samples,publication_p50_ms,publication_p95_ms,provider_p50_ms");
		foreach (var group in samples.GroupBy(x => (x.Variant, x.Count, x.Scenario)))
		{
			var values = group.Select(x => x.PublishedMs).Order().ToArray();
			var providers = group.Select(x => x.ProviderMs).Order().ToArray();
			var mid = values.Length / 2;
			var median = values.Length % 2 == 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
			var providerMedian = providers.Length % 2 == 0 ? (providers[mid - 1] + providers[mid]) / 2 : providers[mid];
			var p95 = values[(int)Math.Ceiling(values.Length * 0.95) - 1];
			await summary.WriteLineAsync(FormattableString.Invariant($"{group.Key.Variant},{group.Key.Count},{group.Key.Scenario},{values.Length},{median:F3},{p95:F3},{providerMedian:F3}"));
		}
		await File.WriteAllTextAsync(Path.Combine(output, "scope.txt"),
			"Source-linked Phase III suggestion scheduling and NativeNameSearch; Original traversal extracted from original FolderSearch and uses original shared shortcut helper; Phase II/Original delay path extracted as 200 ms. Real synthetic NTFS folders; max ten suggestions; warm metadata cache; no icons, ListedItem, WinRT index, dispatcher, XAML, rendering or interactive GUI. PublishedMs ends at the harness callback, not at a visible result. No system cache eviction. Sequential paired variant order.\r\n");
	}

	public static async Task<int> Main(string[] args)
	{
		if (args.Length == 2 && args[0] == "--contracts") return await Contracts(Path.GetFullPath(args[1]));
		if (args.Length == 4 && args[0] == "--bench")
		{
			await Benchmark(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), int.Parse(args[3], CultureInfo.InvariantCulture));
			return 0;
		}
		Console.Error.WriteLine("--contracts output | --bench synthetic-root fresh-output repetitions");
		return 2;
	}
}
