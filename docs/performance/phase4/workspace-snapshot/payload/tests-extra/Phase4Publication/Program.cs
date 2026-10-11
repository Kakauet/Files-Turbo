using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var taskWorkspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(taskWorkspace, "tests-extra/Phase4Publication/source-metadata.json")));
var results = new List<ContractResult>();
VerifyHashes();

await Run("same_references_and_order_emit_no_collection_events_and_retain_consumer_rows", async () =>
{
	var items = Items(99);
	using var shell = new ShellViewModel(items);
	var observer = new PublicationObserver(shell.FilesAndFolders) { SelectedItem = items[23] };
	var originalRows = observer.Rows.ToDictionary(pair => pair.Key, pair => pair.Value);
	var originalImages = items.Select(item => item.FileImage).ToArray();
	shell.SetSource(items.ToList());
	shell.AddPendingSearchResult(new("queued search result"));
	await Publish(shell);
	Assert(observer.Changes.Count == 0, "An unchanged snapshot emitted CollectionChanged.");
	Assert(ReferenceEquals(observer.SelectedItem, items[23]), "The controlled consumer lost its selection.");
	Assert(items.All(item => ReferenceEquals(observer.Rows[item], originalRows[item])), "The controlled consumer rebuilt an existing row.");
	Assert(items.Select(item => item.FileImage).SequenceEqual(originalImages) && items.All(item => item.ItemPropertiesInitialized), "Publication changed item image/property state.");
	AssertSideEffects(shell, 1);
	Assert(shell.PendingSearchCount == 0 && AppMemoryHelper.Activities == 1, "Unchanged publication skipped its common side effects.");
});

await Run("large_unchanged_snapshot_does_not_use_the_append_event_limit", async () =>
{
	var items = Items(2000);
	using var shell = new ShellViewModel(items);
	var observer = new PublicationObserver(shell.FilesAndFolders);
	await Publish(shell);
	Assert(observer.Changes.Count == 0 && shell.FilesAndFolders.Count == 2000, "A large unchanged snapshot caused a Reset.");
});

await Run("tail_64_to_99_emits_35_single_add_events_and_retains_existing_consumer_rows", async () =>
{
	var items = Items(99);
	using var shell = new ShellViewModel(items.Take(64));
	var observer = new PublicationObserver(shell.FilesAndFolders) { SelectedItem = items[8] };
	var existingRows = observer.Rows.ToDictionary(pair => pair.Key, pair => pair.Value);
	var image = items[8].FileImage;
	shell.SetSource(items);
	await Publish(shell);
	AssertSingleAdds(observer, items, 64, 35);
	Assert(ReferenceEquals(observer.SelectedItem, items[8]) && ReferenceEquals(items[8].FileImage, image), "Append changed the selected item or its image.");
	Assert(existingRows.All(pair => ReferenceEquals(observer.Rows[pair.Key], pair.Value)), "Append rebuilt an existing controlled row.");
	AssertSideEffects(shell, 1);
});

await Run("tail_of_128_items_uses_individual_add_events", async () =>
{
	var items = Items(129);
	using var shell = new ShellViewModel(items.Take(1));
	var observer = new PublicationObserver(shell.FilesAndFolders);
	shell.SetSource(items);
	await Publish(shell);
	AssertSingleAdds(observer, items, 1, 128);
});

await Run("tail_of_129_items_falls_back_to_one_reset", async () =>
{
	var items = Items(130);
	using var shell = new ShellViewModel(items.Take(1));
	var observer = new PublicationObserver(shell.FilesAndFolders);
	shell.SetSource(items);
	await Publish(shell);
	AssertOneReset(observer, shell, items);
});

foreach (var mutation in new[] { "reorder", "remove", "replace_same_name_reference", "insert_before_existing" })
{
	await Run($"{mutation}_falls_back_to_one_reset", async () =>
	{
		var items = Items(5);
		var changed = items.ToList();
		switch (mutation)
		{
			case "reorder": (changed[0], changed[1]) = (changed[1], changed[0]); break;
			case "remove": changed.RemoveAt(2); break;
			case "replace_same_name_reference": changed[2] = new(items[2].Name); break;
			case "insert_before_existing": changed.Insert(0, new("inserted")); break;
		}
		using var shell = new ShellViewModel(items);
		var observer = new PublicationObserver(shell.FilesAndFolders);
		shell.SetSource(changed);
		await Publish(shell);
		AssertOneReset(observer, shell, changed);
	});
}

foreach (var grouping in new[] { "collection", "preference", "both" })
{
	await Run($"grouped_{grouping}_unchanged_snapshot_retains_reset_behavior", async () =>
	{
		var items = Items(4);
		using var shell = new ShellViewModel(items);
		if (grouping is "collection" or "both") shell.SetCollectionGrouped();
		if (grouping is "preference" or "both") shell.SetGroupPreference(true);
		var observer = new PublicationObserver(shell.FilesAndFolders);
		await Publish(shell);
		AssertOneReset(observer, shell, items);
		Assert(shell.GroupOrderCalls == (grouping == "collection" ? 0 : 1), "Grouped fallback changed the group ordering side effect.");
	});
}

await Run("first_nonempty_publication_from_empty_uses_one_reset", async () =>
{
	var items = Items(99);
	using var shell = new ShellViewModel();
	var observer = new PublicationObserver(shell.FilesAndFolders);
	shell.SetSource(items);
	await Publish(shell);
	AssertOneReset(observer, shell, items);
});

await Run("empty_to_empty_publication_emits_no_events_but_updates_directory_state", async () =>
{
	using var shell = new ShellViewModel();
	var observer = new PublicationObserver(shell.FilesAndFolders);
	await Publish(shell);
	Assert(observer.Changes.Count == 0 && shell.FilesAndFolders.Count == 0, "An unchanged empty snapshot caused a Reset.");
	AssertSideEffects(shell, 1);
});

await Run("removing_all_items_emits_one_reset", async () =>
{
	using var shell = new ShellViewModel(Items(4));
	var observer = new PublicationObserver(shell.FilesAndFolders);
	shell.SetSource([]);
	await Publish(shell);
	AssertOneReset(observer, shell, []);
});

await Run("case_insensitive_filter_publishes_only_matching_items", async () =>
{
	var items = new[] { new ListedItem("Alpha.jpg"), new ListedItem("beta.jpg"), new ListedItem("ALPHABET.png") };
	using var shell = new ShellViewModel();
	var observer = new PublicationObserver(shell.FilesAndFolders);
	shell.SetSource(items);
	shell.FilesAndFoldersFilter = "aLpHa";
	await Publish(shell);
	AssertOneReset(observer, shell, [items[0], items[2]]);
	AssertSideEffects(shell, 1);
});

await Run("filter_changed_after_snapshot_does_not_publish_stale_results_and_releases_semaphore", async () =>
{
	var old = new ListedItem("previous");
	var alpha = new ListedItem("Alpha");
	var beta = new ListedItem("Beta");
	using var shell = new ShellViewModel([old]);
	var observer = new PublicationObserver(shell.FilesAndFolders);
	shell.SetSource([alpha, beta]);
	shell.FilesAndFoldersFilter = "Alpha";
	shell.AddPendingSearchResult(old);
	var stale = shell.ApplyFilesAndFoldersChangesAsync();
	await WaitForQueued(shell, stale);
	shell.FilesAndFoldersFilter = "Beta";
	Assert(shell.Dispatcher.RunOne(), "Stale dispatcher action was missing.");
	await stale.WaitAsync(TimeSpan.FromSeconds(5));
	Assert(observer.Changes.Count == 0 && ReferenceEquals(shell.FilesAndFolders[0], old), "A stale filter replaced the old collection.");
	Assert(shell.SemaphoreCount == 1 && shell.PendingSearchCount == 1, "Stale filter leaked the semaphore or ran publication side effects.");
	AssertSideEffects(shell, 0);
	Assert(AppMemoryHelper.Activities == 0, "Stale filter marked publication activity.");
	await Publish(shell);
	AssertOneReset(observer, shell, [beta]);
});

foreach (var cancellation in new[] { "request", "navigation" })
{
	await Run($"{cancellation}_canceled_before_dispatch_preserves_collection_and_releases_semaphore", async () =>
	{
		var old = new ListedItem("previous");
		using var shell = new ShellViewModel([old]);
		using var cancel = new CancellationTokenSource();
		var observer = new PublicationObserver(shell.FilesAndFolders);
		shell.SetSource(Items(5));
		shell.AddPendingSearchResult(old);
		var pending = shell.ApplyFilesAndFoldersChangesAsync(cancel.Token);
		await WaitForQueued(shell, pending);
		if (cancellation == "request") cancel.Cancel(); else shell.CancelNavigation();
		Assert(shell.Dispatcher.RunOne(), "Canceled dispatcher action was missing.");
		await pending.WaitAsync(TimeSpan.FromSeconds(5));
		Assert(observer.Changes.Count == 0 && ReferenceEquals(shell.FilesAndFolders[0], old), "Canceled publication changed the bound collection.");
		Assert(shell.SemaphoreCount == 1 && shell.PendingSearchCount == 1, "Canceled publication leaked the semaphore or ran common side effects.");
		AssertSideEffects(shell, 0);
		Assert(AppMemoryHelper.Activities == 0, "Canceled publication marked activity.");
	});
}

await Run("already_canceled_request_does_not_acquire_or_overrelease_publication_semaphore", async () =>
{
	using var shell = new ShellViewModel(Items(2));
	using var cancel = new CancellationTokenSource();
	var observer = new PublicationObserver(shell.FilesAndFolders);
	cancel.Cancel();
	await shell.ApplyFilesAndFoldersChangesAsync(cancel.Token).WaitAsync(TimeSpan.FromSeconds(5));
	Assert(shell.SemaphoreCount == 1 && shell.Dispatcher.PendingCount == 0 && observer.Changes.Count == 0, "Pre-cancellation acquired, leaked or overreleased the semaphore.");
	AssertSideEffects(shell, 0);
});

await Run("filter_and_source_are_captured_after_acquiring_the_publication_semaphore", async () =>
{
	var first = new ListedItem("Beta-first");
	var tail = new ListedItem("Beta-tail");
	using var shell = new ShellViewModel([first]);
	var observer = new PublicationObserver(shell.FilesAndFolders);
	await shell.HoldPublicationAsync();
	shell.SetSource([new("Alpha-old")]);
	shell.FilesAndFoldersFilter = "Alpha";
	var waiting = shell.ApplyFilesAndFoldersChangesAsync();
	Assert(shell.Dispatcher.PendingCount == 0 && !waiting.IsCompleted, "A waiting publication bypassed the semaphore.");
	shell.SetSource([first, new("Alpha-discarded"), tail]);
	shell.FilesAndFoldersFilter = "Beta";
	shell.ReleasePublication();
	await Pump(shell, waiting);
	AssertSingleAdds(observer, [first, tail], 1, 1);
	Assert(shell.SemaphoreCount == 1, "Serial filtered publication leaked the semaphore.");
});

await Run("waiting_followup_append_snapshots_the_latest_source_after_previous_publication", async () =>
{
	var items = Items(100);
	using var shell = new ShellViewModel(items.Take(64));
	var observer = new PublicationObserver(shell.FilesAndFolders);
	shell.SetSource(items.Take(99));
	var first = shell.ApplyFilesAndFoldersChangesAsync();
	await WaitForQueued(shell, first);
	var followup = shell.ApplyFilesAndFoldersChangesAsync();
	Assert(shell.Dispatcher.PendingCount == 1 && !followup.IsCompleted, "Followup did not wait for the first publication.");
	shell.AddSourceItem(items[99]);
	Assert(shell.Dispatcher.RunOne(), "First publication action was missing.");
	await first.WaitAsync(TimeSpan.FromSeconds(5));
	Assert(shell.FilesAndFolders.Count == 99, "The first snapshot unexpectedly included later source changes.");
	await Pump(shell, followup);
	AssertSingleAdds(observer, items, 64, 36);
	Assert(shell.FilesAndFolders.Count == 100 && shell.SemaphoreCount == 1, "Followup used a stale snapshot or leaked the semaphore.");
	AssertSideEffects(shell, 2);
});

await Run("publication_diagnostics_are_disabled_without_opt_in", async () =>
{
	var items = Items(2);
	using var shell = new ShellViewModel(items.Take(1));
	await Publish(shell);
	shell.SetSource(items);
	await Publish(shell);
	shell.SetSource([new("replacement")]);
	await Publish(shell);
	Assert(UiPerformanceTrace.Entries.Count == 0, "Diagnostics emitted markers without opt-in.");
});

await Run("opt_in_diagnostics_distinguish_unchanged_append_and_reset_publications", async () =>
{
	UiPerformanceTrace.IsEnabled = true;
	var items = Items(2);
	using var shell = new ShellViewModel(items.Take(1)) { PerformanceOperationId = 77 };
	await Publish(shell);
	shell.SetSource(items);
	await Publish(shell);
	shell.SetSource([new("replacement")]);
	await Publish(shell);
	var entries = UiPerformanceTrace.Entries;
	Assert(entries.Count(entry => entry.Milestone == "DataPublicationUnchanged") == 1, "Unchanged marker was missing or repeated.");
	var appended = entries.Single(entry => entry.Milestone == "DataPublicationAppended");
	Assert(appended.Count == 2 && appended.Value == 1, "Append marker did not report total and appended counts.");
	Assert(entries.Count(entry => entry.Milestone == "DataPublicationReset") == 1, "Reset marker was missing or repeated.");
	Assert(entries.Count(entry => entry.Milestone == "DataPublished") == 3 && entries.Count(entry => entry.Milestone == "PublicationQueueDelay") == 3, "Common publication timing markers were skipped.");
	Assert(entries.All(entry => entry.Operation == 77 && entry.DurationMs >= 0), "Diagnostics mixed operation IDs or invalid durations.");
});

VerifyHashes();
var outputPath = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(taskWorkspace, "reports/phase4-publication-contracts.json"));
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
var report = new
{
	Source = metadata.RootElement.Clone(),
	Contracts = results,
	Passed = results.Count(result => result.Passed),
	Total = results.Count,
	Scope = "Literal production publication/filter methods; linked real collection notifications. Controlled dispatcher, grouped header models and Reset-sensitive consumer. Row/selection checks concern this consumer; excludes WinUI rendering, native collection subscribers and physical paint timing."
};
File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }).Replace("\n", "\r\n") + "\r\n");
Console.WriteLine($"Passed {report.Passed}/{report.Total} publication contracts. {outputPath}");
if (report.Passed != report.Total)
	throw new Exception("Publication contracts failed; inspect the report.");

async Task Run(string name, Func<Task> test)
{
	UiPerformanceTrace.IsEnabled = false;
	UiPerformanceTrace.Entries.Clear();
	AppMemoryHelper.Activities = 0;
	App.Logger.Errors.Clear();
	var stopwatch = Stopwatch.StartNew();
	try
	{
		await test();
		Assert(App.Logger.Errors.Count == 0, "Production logged an unexpected publication failure.");
		results.Add(new(name, true, stopwatch.Elapsed.TotalMilliseconds, null));
	}
	catch (Exception error)
	{
		results.Add(new(name, false, stopwatch.Elapsed.TotalMilliseconds, error.ToString()));
	}
	finally
	{
		UiPerformanceTrace.IsEnabled = false;
	}
}

void VerifyHashes()
{
	var source = metadata.RootElement;
	Assert(Hash(source.GetProperty("SourcePath").GetString()!) == source.GetProperty("SourceSha256").GetString(), "ShellViewModel changed after extraction; prepare and rebuild.");
	Assert(PublicationBuildStamp.SourceSha256 == source.GetProperty("SourceSha256").GetString(), "The executable was compiled from another production source snapshot; rebuild.");
	Assert(Hash(source.GetProperty("GeneratedPath").GetString()!) == source.GetProperty("GeneratedSha256").GetString(), "Generated literal methods changed after preparation; prepare and rebuild.");
	var linked = source.GetProperty("LinkedFiles").EnumerateArray().ToArray();
	foreach (var file in linked)
		Assert(Hash(file.GetProperty("Path").GetString()!) == file.GetProperty("Sha256").GetString(), "A linked production collection file changed after preparation; prepare and rebuild.");
	var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', linked.Select(file => file.GetProperty("Sha256").GetString())))));
	Assert(fingerprint == source.GetProperty("LinkedFingerprint").GetString() && fingerprint == PublicationBuildStamp.LinkedFingerprint, "The compiled linked-collection fingerprint differs from the prepared snapshot; rebuild.");
}

static ListedItem[] Items(int count) => Enumerable.Range(0, count).Select(index => new ListedItem($"item-{index:D4}") { FileImage = new object(), ItemPropertiesInitialized = true }).ToArray();
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static void AssertSideEffects(ShellViewModel shell, int count)
{
	Assert(shell.EmptyTextUpdates == count && shell.NetworkInfoUpdates == count && shell.DirectoryNotifications == count, "Common publication side effects were skipped, repeated or ran after rejection.");
}
static void AssertSingleAdds(PublicationObserver observer, IReadOnlyList<ListedItem> items, int start, int count)
{
	Assert(observer.Changes.Count == count, "Append emitted the wrong number of collection events.");
	for (var index = 0; index < count; index++)
	{
		var change = observer.Changes[index];
		Assert(change.Action == NotifyCollectionChangedAction.Add && change.NewItems?.Count == 1 && change.NewStartingIndex == start + index && ReferenceEquals(change.NewItems[0], items[start + index]), "Append event was a Reset, a multi-item Add, out of order or contained the wrong reference.");
	}
	Assert(observer.Rows.Count == items.Count, "The controlled consumer did not receive the complete appended collection.");
}
static void AssertOneReset(PublicationObserver observer, ShellViewModel shell, IEnumerable<ListedItem> expected)
{
	Assert(observer.Changes.Count == 1 && observer.Changes[0].Action == NotifyCollectionChangedAction.Reset, "Fallback did not emit exactly one Reset.");
	Assert(shell.FilesAndFolders.SequenceEqual(expected, ReferenceEqualityComparer.Instance), "Fallback published the wrong item references or order.");
	Assert(shell.SemaphoreCount == 1, "Fallback leaked its publication semaphore.");
}
static Task Publish(ShellViewModel shell) => Pump(shell, shell.ApplyFilesAndFoldersChangesAsync());
static async Task Pump(ShellViewModel shell, Task pending)
{
	var deadline = Stopwatch.StartNew();
	while (!pending.IsCompleted)
	{
		Assert(deadline.Elapsed < TimeSpan.FromSeconds(5), "Controlled publication pump timed out.");
		if (!shell.Dispatcher.RunOne()) await Task.Delay(1);
	}
	await pending;
}
static async Task WaitForQueued(ShellViewModel shell, Task pending)
{
	var deadline = Stopwatch.StartNew();
	while (shell.Dispatcher.PendingCount == 0)
	{
		Assert(!pending.IsCompleted, "Publication completed before reaching the controlled dispatcher.");
		Assert(deadline.Elapsed < TimeSpan.FromSeconds(5), "Publication did not reach the controlled dispatcher.");
		await Task.Delay(1);
	}
}

internal sealed record ContractResult(string Name, bool Passed, double DurationMs, string? Error);
