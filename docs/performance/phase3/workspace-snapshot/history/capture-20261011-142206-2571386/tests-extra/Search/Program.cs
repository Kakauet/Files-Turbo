using System.Diagnostics;
using System.IO.Enumeration;
using System.Text.Json;
using Files.App.Utils.Storage;

internal static class Program
{
	private sealed record Contract(string Name, string Status, string Detail);
	private static readonly List<Contract> Contracts = [];
	private static void Test(string name, Action action)
	{
		try { action(); Contracts.Add(new(name, "passed", "")); }
		catch (Exception ex) { Contracts.Add(new(name, "failed", ex.ToString())); }
	}
	private static void Assert(bool condition, string reason)
	{
		if (!condition) throw new InvalidOperationException(reason);
	}
	private static void Throws<T>(Action action) where T : Exception
	{
		try { action(); } catch (T) { return; }
		throw new InvalidOperationException($"Expected {typeof(T).Name}");
	}
	private static List<NameSearchEntry> Scan(string folder, string pattern, bool hidden = false, bool system = false,
		bool dots = false, bool supplement = false, CancellationToken token = default)
		=> NativeNameSearch.Enumerate(folder, FileSystemName.TranslateWin32Expression(pattern), hidden, system, dots, supplement, token).Where(x => x.IsMatch).ToList();
	private static void AssertMissingScope(string root)
	{
		var incomplete = false;
		var results = NativeNameSearch.Enumerate(Path.Combine(root, "missing"), "*", false, false, false, false, default, () => incomplete = true).ToList();
		Assert(results.Count == 0 && incomplete, "Missing scope was silently declared exhaustive");
	}
	public static async Task<int> Main(string[] args)
	{
		if (args.Length != 2) return 2;
		var fixture = Path.GetFullPath(args[0]);
		var output = Path.GetFullPath(args[1]);
		if (Directory.Exists(fixture)) throw new InvalidOperationException("Fixture must be a fresh workspace path.");
		Directory.CreateDirectory(fixture);
		Directory.CreateDirectory(output);
		File.WriteAllText(Path.Combine(fixture, "Alpha.txt"), "test bytes");
		File.WriteAllText(Path.Combine(fixture, "beta.txt"), "beta");
		File.WriteAllText(Path.Combine(fixture, "é_東京_ñ.TXT"), "unicode");
		File.WriteAllText(Path.Combine(fixture, ".dot.txt"), "dot");
		File.WriteAllText(Path.Combine(fixture, "hidden.txt"), "hidden");
		File.SetAttributes(Path.Combine(fixture, "hidden.txt"), FileAttributes.Hidden);
		File.WriteAllText(Path.Combine(fixture, "protected.txt"), "system");
		File.SetAttributes(Path.Combine(fixture, "protected.txt"), FileAttributes.Hidden | FileAttributes.System);
		File.WriteAllText(Path.Combine(fixture, "link.LNK"), "synthetic shortcut name, not parsed");
		File.WriteAllText(Path.Combine(fixture, "website.URL"), "[InternetShortcut]\r\nURL=https://example.invalid/\r\n");
		File.WriteAllText(Path.Combine(fixture, "NoExtension"), "no extension");
		Directory.CreateDirectory(Path.Combine(fixture, "subdir"));
		Directory.CreateDirectory(Path.Combine(fixture, "directory.LNK"));
		Directory.CreateDirectory(Path.Combine(fixture, ".dotdir"));
		File.WriteAllText(Path.Combine(fixture, "subdir", "inside.txt"), "subdirectory");

		Test("case-insensitive exact name", () => Assert(Scan(fixture, "alpha.TXT").Single().Name == "Alpha.txt", "Case mismatch"));
		Test("partial name", () => Assert(Scan(fixture, "*pha*").Single().Name == "Alpha.txt", "Substring mismatch"));
		Test("Unicode name", () => Assert(Scan(fixture, "*東京*").Single().Name == "é_東京_ñ.TXT", "Unicode mismatch"));
		Test("zero results", () => Assert(Scan(fixture, "*no-such-name*").Count == 0, "Unexpected match"));
		Test("result dedup normalizes only selected root prefix", () => Assert(NativeNameSearch.GetResultPathKey("c:\\scope\\Alpha.txt", "C:\\Scope") == "C:\\Scope\\Alpha.txt", "Root prefix casing not reconciled"));
		Test("result dedup preserves case-sensitive descendants", () => Assert(NativeNameSearch.GetResultPathKey("c:\\scope\\Alpha.txt", "C:\\Scope") != NativeNameSearch.GetResultPathKey("C:\\Scope\\alpha.txt", "C:\\Scope"), "Distinct descendant names collapsed"));
		Test("result dedup does not leak prefix siblings", () => Assert(NativeNameSearch.GetResultPathKey("c:\\scope-other\\Alpha.txt", "C:\\Scope") == "c:\\scope-other\\Alpha.txt", "Sibling scope normalized"));
		Test("DOS wildcard question", () => Assert(Scan(fixture, "?lpha.txt").Single().Name == "Alpha.txt", "Wildcard mismatch"));
		Test("DOS wildcard star dot star includes extensionless files", () => Assert(Scan(fixture, "*.*").Any(x => x.Name == "NoExtension"), "Win32 extensionless behavior lost"));
		Test("extensions case-insensitive", () => Assert(Scan(fixture, "*.txt").Count == 3, "Wrong default visible txt files"));
		Test("hidden default omitted", () => Assert(Scan(fixture, "hidden.txt").Count == 0, "Hidden leaked"));
		Test("hidden enabled", () => Assert(Scan(fixture, "hidden.txt", hidden: true).Count == 1, "Hidden not included"));
		Test("protected needs both flags", () => Assert(Scan(fixture, "protected.txt", hidden: true).Count == 0 && Scan(fixture, "protected.txt", hidden: true, system: true).Count == 1, "Protected mismatch"));
		Test("dot default omitted", () => Assert(Scan(fixture, ".dot.txt").Count == 0, "Dot file leaked"));
		Test("dot enabled", () => Assert(Scan(fixture, ".dot.txt", dots: true).Count == 1, "Dot file not included"));
		Test("supplement shortcuts only", () => Assert(Scan(fixture, "*", supplement: true).Select(x => x.Name).Order().SequenceEqual(new[] { "link.LNK", "website.URL" }), "Supplement leaked visible file or directory"));
		Test("supplement hidden obeys visibility", () => Assert(Scan(fixture, "*.txt", hidden: true, supplement: true).Select(x => x.Name).SequenceEqual(new[] { "hidden.txt" }), "Wrong hidden supplement"));
		Test("directories available to traversal even without match", () => Assert(NativeNameSearch.Enumerate(fixture, "no-match", false, false, false, false, default).Count(x => (x.Attributes & FileAttributes.Directory) != 0) == 3, "Directory traversal lost"));
		Test("scope does not traverse itself", () => Assert(Scan(fixture, "inside.txt").Count == 0, "Out of single-directory scope"));
		Test("metadata is enumeration metadata", () =>
		{
			var entry = Scan(fixture, "Alpha.txt").Single();
			var info = new FileInfo(entry.Path);
			Assert(entry.Length == info.Length && entry.Modified.UtcDateTime == info.LastWriteTimeUtc && entry.Created.UtcDateTime == info.CreationTimeUtc, "Metadata mismatch");
			Assert(entry.Path == Path.Combine(fixture, "Alpha.txt"), "Noncanonical scope");
		});
		Test("pre-cancellation", () => { using var cts = new CancellationTokenSource(); cts.Cancel(); Throws<OperationCanceledException>(() => Scan(fixture, "*", token: cts.Token)); });
		Test("cancel during iteration", () =>
		{
			using var cts = new CancellationTokenSource();
			using var iterator = NativeNameSearch.Enumerate(fixture, "*", true, true, true, false, cts.Token).GetEnumerator();
			Assert(iterator.MoveNext(), "No first entry"); cts.Cancel();
			Throws<OperationCanceledException>(() => iterator.MoveNext());
		});
		Test("missing scope reports incomplete", () => AssertMissingScope(fixture));
		Test("rename is fresh without cached stale path", () =>
		{
			File.Move(Path.Combine(fixture, "beta.txt"), Path.Combine(fixture, "renamed.txt"));
			Assert(Scan(fixture, "beta.txt").Count == 0 && Scan(fixture, "renamed.txt").Count == 1, "Rename stale");
		});
		Test("delete is fresh without cached stale path", () =>
		{
			File.Delete(Path.Combine(fixture, "renamed.txt"));
			Assert(Scan(fixture, "renamed.txt").Count == 0, "Delete stale");
		});
		Test("long Unicode path", () =>
		{
			var longFolder = Path.Combine(fixture, new string('a', 90), new string('b', 90), new string('c', 90));
			Directory.CreateDirectory(longFolder);
			File.WriteAllText(Path.Combine(longFolder, "東京.txt"), "long");
			Assert(Scan(longFolder, "*東京*").Single().Path.Length > 260, "Long path not tested");
		});

		try
		{
			var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Scan(fixture, "Alpha.txt").Single().Name)));
			Assert(results.All(x => x == "Alpha.txt"), "Concurrent query data race");
			Contracts.Add(new("simultaneous independent queries", "passed", "12 filesystem queries"));
		}
		catch (Exception ex) { Contracts.Add(new("simultaneous independent queries", "failed", ex.ToString())); }

		Test("bounded handle and managed-memory soak", () =>
		{
			for (var i = 0; i < 10; i++) _ = Scan(fixture, "*");
			using var process = Process.GetCurrentProcess();
			GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
			process.Refresh(); var handlesBefore = process.HandleCount; var memoryBefore = GC.GetTotalMemory(true);
			for (var i = 0; i < 100; i++)
			{
				_ = Scan(fixture, "*");
				using var enumerator = NativeNameSearch.Enumerate(fixture, "*", false, false, false, false, default).GetEnumerator();
				_ = enumerator.MoveNext();
			}
			GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); process.Refresh();
			Assert(process.HandleCount <= handlesBefore + 8, "Directory handles retained");
			Assert(GC.GetTotalMemory(true) < memoryBefore + 4 * 1024 * 1024, "Unexpected managed growth");
		});

		File.WriteAllText(Path.Combine(output, "search-contracts.json"), JsonSerializer.Serialize(Contracts, new JsonSerializerOptions { WriteIndented = true }));
		Console.WriteLine($"Search contracts: {Contracts.Count(x => x.Status == "passed")} passed, {Contracts.Count(x => x.Status == "failed")} failed. Synthetic fixture retained: {fixture}");
		return Contracts.Any(x => x.Status == "failed") ? 1 : 0;
	}
}
