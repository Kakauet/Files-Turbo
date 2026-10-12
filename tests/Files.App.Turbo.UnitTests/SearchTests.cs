// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO.Enumeration;

namespace Files.App.Turbo.UnitTests
{
	[TestClass]
	public sealed class SearchTests
	{
		private static string root = string.Empty;

		[ClassInitialize]
		public static void CreateTree(TestContext _)
		{
			root = Path.Combine(Path.GetTempPath(), "FilesTurboTests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			foreach (var name in new[] { "report.txt", "Report final.docx", "café.txt", "日本語のメモ.txt", "😀 emoji.txt", "naïve.md", ".hidden-dot.txt", "archive.zip", "link.lnk", "web.url" })
				File.WriteAllText(Path.Combine(root, name), name);

			var hidden = Path.Combine(root, "hidden.txt");
			File.WriteAllText(hidden, "h");
			File.SetAttributes(hidden, FileAttributes.Hidden);
			var system = Path.Combine(root, "system.txt");
			File.WriteAllText(system, "s");
			File.SetAttributes(system, FileAttributes.Hidden | FileAttributes.System);

			// A tree for the parallel walk: 3 levels, 4 children each
			void Fill(string folder, int depth)
			{
				if (depth == 0)
					return;
				for (var i = 0; i < 4; i++)
				{
					var child = Path.Combine(folder, $"d{depth}-{i}");
					Directory.CreateDirectory(child);
					File.WriteAllText(Path.Combine(child, $"report-{depth}-{i}.txt"), "x");
					Fill(child, depth - 1);
				}
			}
			Fill(Path.Combine(root, "tree"), 3);
		}

		[ClassCleanup]
		public static void DeleteTree()
		{
			foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
				File.SetAttributes(file, FileAttributes.Normal);
			Directory.Delete(root, recursive: true);
		}

		private static List<NameSearchEntry> Search(string query, bool showHidden = false, bool showSystem = false, bool showDot = false, string? folder = null)
			=> NativeNameSearch.Enumerate(folder ?? root, FileSystemName.TranslateWin32Expression($"*{query}*"), showHidden, showSystem, showDot,
				hiddenOnly: false, CancellationToken.None).Where(entry => entry.IsMatch).ToList();

		[TestMethod]
		public void Partial_match_is_case_insensitive()
		{
			var names = Search("report").Select(entry => entry.Name).ToList();
			CollectionAssert.AreEquivalent(new[] { "report.txt", "Report final.docx" }, names);
		}

		[TestMethod]
		public void Unicode_names_match_exactly_and_partially()
		{
			Assert.AreEqual("café.txt", Search("café").Single().Name);
			Assert.AreEqual("日本語のメモ.txt", Search("メモ").Single().Name);
			Assert.AreEqual("😀 emoji.txt", Search("😀").Single().Name);
			Assert.AreEqual("naïve.md", Search("NAÏVE").Single().Name);
		}

		[TestMethod]
		public void Hidden_system_and_dot_files_follow_the_settings()
		{
			Assert.IsFalse(Search("hidden").Any(entry => entry.Name == "hidden.txt"));
			Assert.IsTrue(Search("hidden", showHidden: true).Any(entry => entry.Name == "hidden.txt"));
			Assert.IsFalse(Search("system", showHidden: true).Any());
			Assert.IsTrue(Search("system", showHidden: true, showSystem: true).Any());
			Assert.IsFalse(Search("hidden-dot").Any());
			Assert.IsTrue(Search("hidden-dot", showDot: true).Any());
		}

		[TestMethod]
		public void Folders_are_returned_for_recursion_even_when_their_name_does_not_match()
		{
			var all = NativeNameSearch.Enumerate(root, FileSystemName.TranslateWin32Expression("*no-such-name*"), false, false, false, false, CancellationToken.None).ToList();
			Assert.IsTrue(all.Any(entry => entry.Name == "tree" && !entry.IsMatch && (entry.Attributes & FileAttributes.Directory) != 0));
			Assert.IsFalse(all.Any(entry => entry.IsMatch));
		}

		[TestMethod]
		public void Entry_carries_size_and_times()
		{
			var entry = Search("café").Single();
			Assert.AreEqual(new FileInfo(Path.Combine(root, "café.txt")).Length, entry.Length);
			Assert.AreEqual(File.GetLastWriteTimeUtc(entry.Path), entry.Modified.UtcDateTime);
		}

		[TestMethod]
		public void Missing_folder_reports_incomplete_results()
		{
			var incomplete = false;
			var entries = NativeNameSearch.Enumerate(Path.Combine(root, "missing"), "*", false, false, false, false, CancellationToken.None, () => incomplete = true).ToList();
			Assert.AreEqual(0, entries.Count);
			Assert.IsTrue(incomplete);
		}

		[TestMethod]
		public void Shortcut_detection_covers_lnk_and_url_only()
		{
			Assert.IsTrue(NativeNameSearch.IsShortcut("a.LNK"));
			Assert.IsTrue(NativeNameSearch.IsShortcut("a.url"));
			Assert.IsFalse(NativeNameSearch.IsShortcut("a.lnk.txt"));
			Assert.IsFalse(NativeNameSearch.IsShortcut("lnk"));
		}

		[TestMethod]
		public void Result_keys_unify_root_casing_but_keep_case_sensitive_names_distinct()
		{
			const string rootPath = @"C:\Data";
			Assert.AreEqual(NativeNameSearch.GetResultPathKey(@"c:\data\File.txt", rootPath), NativeNameSearch.GetResultPathKey(@"C:\Data\File.txt", rootPath));
			Assert.AreNotEqual(NativeNameSearch.GetResultPathKey(@"C:\Data\file.txt", rootPath), NativeNameSearch.GetResultPathKey(@"C:\Data\File.txt", rootPath));
			// A sibling folder that only shares a prefix is not rewritten
			Assert.AreEqual(@"c:\database\x", NativeNameSearch.GetResultPathKey(@"c:\database\x", rootPath));
		}

		[TestMethod]
		public async Task Parallel_walk_visits_every_folder_once()
		{
			var visited = new ConcurrentBag<string>();
			await ParallelDirectoryWork.RunAsync((Path.Combine(root, "tree"), false), 4, (folder, enqueue, token) =>
			{
				visited.Add(folder.Path);
				foreach (var child in Directory.EnumerateDirectories(folder.Path))
					enqueue((child, false));
			}, CancellationToken.None);

			var expected = Directory.EnumerateDirectories(Path.Combine(root, "tree"), "*", SearchOption.AllDirectories).Append(Path.Combine(root, "tree")).ToList();
			Assert.AreEqual(expected.Count, visited.Count);
			CollectionAssert.AreEquivalent(expected, visited.ToList());
		}

		[TestMethod]
		public async Task Parallel_walk_surfaces_failures()
		{
			await Assert.ThrowsExactlyAsync<IOException>(() => ParallelDirectoryWork.RunAsync((Path.Combine(root, "tree"), false), 4, (folder, enqueue, token) =>
			{
				foreach (var child in Directory.EnumerateDirectories(folder.Path))
					enqueue((child, false));
				if (folder.Path.EndsWith("d2-1", StringComparison.Ordinal))
					throw new IOException("unreadable");
			}, CancellationToken.None));
		}

		[TestMethod]
		public async Task Parallel_walk_stops_when_cancelled()
		{
			using var cancellation = new CancellationTokenSource();
			var visited = 0;
			var walk = ParallelDirectoryWork.RunAsync((Path.Combine(root, "tree"), false), 2, (folder, enqueue, token) =>
			{
				if (Interlocked.Increment(ref visited) == 3)
					cancellation.Cancel();
				foreach (var child in Directory.EnumerateDirectories(folder.Path))
					enqueue((child, false));
			}, cancellation.Token);

			await Assert.ThrowsAsync<OperationCanceledException>(() => walk);
			Assert.IsTrue(visited < 85, $"Visited {visited} folders after cancellation.");
		}

		[TestMethod]
		public void Suggestion_narrowing_reuses_only_safe_extensions_of_the_previous_query()
		{
			Assert.IsTrue(SearchSuggestionNarrowing.CanReuse("rep", "repo", @"C:\a", @"C:\a"));
			Assert.IsFalse(SearchSuggestionNarrowing.CanReuse("rep", "repo", @"C:\a", @"C:\b"));
			Assert.IsFalse(SearchSuggestionNarrowing.CanReuse("repo", "rep", @"C:\a", @"C:\a"));
			Assert.IsFalse(SearchSuggestionNarrowing.CanReuse("rep", "rep*", @"C:\a", @"C:\a"));
			Assert.IsFalse(SearchSuggestionNarrowing.CanReuse("", "a", @"C:\a", @"C:\a"));

			var filtered = SearchSuggestionNarrowing.Filter(new[] { "Report.txt", "other", "REPORT final" }, "report", name => name);
			CollectionAssert.AreEqual(new[] { "Report.txt", "REPORT final" }, filtered);
		}

		[TestMethod]
		public async Task Suggestion_pipeline_drops_results_for_a_superseded_query()
		{
			var pipeline = new SearchSuggestionPipeline();
			var current = true;
			var published = false;
			await pipeline.RunAsync<string>("abc", () => { current = false; return Task.FromResult<IReadOnlyList<string>>(["abc"]); },
				() => current, _ => published = true, (_, _) => Task.CompletedTask, CancellationToken.None);
			Assert.IsFalse(published);
		}

		[TestMethod]
		public async Task Suggestion_pipeline_publishes_then_enriches_current_results()
		{
			var pipeline = new SearchSuggestionPipeline();
			var steps = new List<string>();
			await pipeline.RunAsync<string>("abc", () => Task.FromResult<IReadOnlyList<string>>(["abc"]),
				() => true, _ => steps.Add("publish"), (_, _) => { steps.Add("enrich"); return Task.CompletedTask; }, CancellationToken.None);
			CollectionAssert.AreEqual(new[] { "publish", "enrich" }, steps);
		}
	}
}
