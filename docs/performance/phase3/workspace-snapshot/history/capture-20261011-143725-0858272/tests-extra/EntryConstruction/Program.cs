using System.Text.Json;
using EntryConstruction.Contracts;
using EntryConstruction.Generated;
using OldExtensions = Files.Shared.Helpers.Original.FileExtensionHelpers;
using NewExtensions = Files.Shared.Helpers.Current.FileExtensionHelpers;

if (args.Length != 2 || args[0] != "--output") throw new ArgumentException("Use --output with a fresh directory in the workspace.");
var output = Path.GetFullPath(args[1]);
var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if (!output.StartsWith(workspace.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must remain inside the workspace.");
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; preserve previous evidence.");
Directory.CreateDirectory(output);
var fixture = Directory.CreateDirectory(Path.Combine(output, "fixture")).FullName;
var knownFiles = Directory.CreateDirectory(Path.Combine(fixture, "known-files")).FullName;
var previousDirectory = Directory.GetCurrentDirectory();
var results = new List<ContractResult>();
long oldNameBytes = 0, newNameBytes = 0, oldArchiveBytes = 0, newArchiveBytes = 0;
try
{
    Directory.SetCurrentDirectory(fixture);
    Test("known normal file classification performs no filesystem probe", () =>
    {
        foreach (var name in new[] { "alpha.txt", "plain", "東京.dat", "file.lnk.txt", "name.", ".hidden" })
        {
            File.WriteAllText(Path.Combine(knownFiles, name), "fixture");
            ProbeDirectory.Calls = 0;
            var oldKind = OriginalClassification.Classify(name, knownFiles);
            Assert(ProbeDirectory.Calls == 1, "Original known-file path did not perform its directory probe.");
            ProbeDirectory.Calls = 0;
            Assert(CurrentClassification.Classify(name, knownFiles) == oldKind, "Normal file classification changed.");
            Assert(ProbeDirectory.Calls == 0, "Current known-file classification touched the filesystem.");
        }
    });
    Test("known shortcut and URL classification preserves extension semantics", () =>
    {
        foreach (var name in new[] { "one.lnk", "two.URL", "three.LnK", ".lnk", "four.lnk.url", "five.url.lnk", "name..url", "trailing.lnk." })
        {
            ProbeDirectory.Calls = 0;
            var expected = OriginalClassification.Classify(name, knownFiles);
            ProbeDirectory.Calls = 0;
            Assert(CurrentClassification.Classify(name, knownFiles) == expected, $"Link classification differs: {name}");
            Assert(ProbeDirectory.Calls == 0, "Known shortcut classification performed a directory probe.");
        }
    });
    Test("unrelated working-directory folder cannot suppress a known shortcut file", () =>
    {
        Directory.CreateDirectory(Path.Combine(fixture, "collision.lnk"));
        File.WriteAllText(Path.Combine(knownFiles, "collision.lnk"), "fixture");
        Assert(!OriginalClassification.Classify("collision.lnk", knownFiles).IsShortcut, "Original CWD collision was not reproduced.");
        ProbeDirectory.Calls = 0;
        Assert(CurrentClassification.Classify("collision.lnk", knownFiles) == new LinkKind(true, false), "Known file was misclassified by an unrelated directory.");
        Assert(ProbeDirectory.Calls == 0, "CWD collision fix still probes directories.");
    });
    Test("archive substring and extension precedence matches Phase II", () =>
    {
        string?[] paths = [null, "", " ", "plain.txt", ".ZIP", "archive.zip", "archive.ZIP\\child", "contains.zip.note", "a.jar\\b.zip", "a.GZ\\b.TAR", "東京.rar", "a.7z.backup", "notzip", "file.mrpack", "archive.lzh", "a.tar.gz"];
        foreach (var path in paths)
        {
            var oldMatch = OldExtensions.IsBrowsableZipFile(path, out var oldExt);
            var newMatch = NewExtensions.IsBrowsableZipFile(path, out var newExt);
            Assert(oldMatch == newMatch && oldExt == newExt, $"Archive substring semantics differ: {path}");
        }
        Assert(NewExtensions.IsBrowsableZipFile("a.jar\\b.zip", out var precedence) && precedence == ".zip", "Fixed extension priority changed.");
    });
    Test("archive files and archive-named real directories retain IsZipPath semantics", () =>
    {
        File.WriteAllText(Path.Combine(fixture, "archive.zip"), "fixture");
        Directory.CreateDirectory(Path.Combine(fixture, "directory.zip"));
        File.WriteAllText(Path.Combine(fixture, "directory.zip", "regular.txt"), "fixture");
        string[] paths = ["archive.zip", "directory.zip", "directory.zip\\regular.txt", "absent.zip\\child.txt", "contains.zip.note", "a.jar\\b.zip", "a.tar.gz", "plain.txt"];
        foreach (var relative in paths)
            foreach (var includeRoot in new[] { false, true })
            {
                var path = Path.Combine(fixture, relative);
                Assert(OriginalZip.IsZipPath(path, includeRoot) == CurrentZip.IsZipPath(path, includeRoot), $"IsZipPath differs: {relative}/{includeRoot}");
            }
        Assert(CurrentZip.IsZipPath(Path.Combine(fixture, "archive.zip")), "A known archive file lost root classification.");
        Assert(!CurrentZip.IsZipPath(Path.Combine(fixture, "directory.zip")), "Archive-named real directory became an archive.");
        Assert(!CurrentZip.IsZipPath(Path.Combine(fixture, "directory.zip", "regular.txt")), "Existing child in archive-named directory became a virtual archive path.");
    });
    Test("ListedItem Name matches Phase II for file and folder settings", () =>
    {
        string?[] names = [null, "", ".", "..", ".hidden", "plain", "alpha.txt", "two.TXT", "東京.é.tar.gz", "name.", "a.b.c", "folder.zip"];
        foreach (var showExtensions in new[] { false, true })
            foreach (var kind in new[] { StorageItemTypes.File, StorageItemTypes.Folder })
                foreach (var name in names)
                {
                    Settings.UserSettingsService.FoldersSettingsService.ShowFileExtensions = showExtensions;
                    var oldItem = new OriginalListedItem { ItemNameRaw = name, PrimaryItemAttribute = kind };
                    var newItem = new CurrentListedItem { ItemNameRaw = name, PrimaryItemAttribute = kind };
                    Assert(oldItem.Name == newItem.Name, $"Name differs: {name}/{showExtensions}/{kind}");
                }
    });
    Test("Name observes extension setting changes and renames without a cache", () =>
    {
        var item = new CurrentListedItem { ItemNameRaw = "first.txt", PrimaryItemAttribute = StorageItemTypes.File };
        Settings.UserSettingsService.FoldersSettingsService.ShowFileExtensions = true;
        Assert(item.Name == "first.txt", "Extension setting true was ignored.");
        Settings.UserSettingsService.FoldersSettingsService.ShowFileExtensions = false;
        Assert(item.Name == "first", "Extension setting false was ignored.");
        item.ItemNameRaw = "second.dat";
        Assert(item.Name == "second", "Renamed item retained old basename.");
        Settings.UserSettingsService.FoldersSettingsService.ShowFileExtensions = true;
        Assert(item.Name == "second.dat", "Renamed item retained hidden extension.");
    });
    Test("shown file extensions avoid basename allocations", () =>
    {
        var raw = "representative_東京_0123456789.txt";
        Settings.UserSettingsService.FoldersSettingsService.ShowFileExtensions = true;
        var oldItem = new OriginalListedItem { ItemNameRaw = raw, PrimaryItemAttribute = StorageItemTypes.File };
        var newItem = new CurrentListedItem { ItemNameRaw = raw, PrimaryItemAttribute = StorageItemTypes.File };
        oldNameBytes = MeasureAllocation(() => Settings.ObservedName = oldItem.Name);
        newNameBytes = MeasureAllocation(() => Settings.ObservedName = newItem.Name);
        Assert(ReferenceEquals(newItem.Name, raw), "Shown name did not reuse its original string.");
        Assert(newNameBytes == 0 && oldNameBytes > newNameBytes, "Expected basename allocation reduction was not observed.");
    });
    Test("archive classification avoids per-call array and closure allocations", () =>
    {
        var path = Path.Combine(knownFiles, "plain.txt");
        oldArchiveBytes = MeasureAllocation(() => Settings.ObservedFlag = OldExtensions.IsBrowsableZipFile(path, out _));
        newArchiveBytes = MeasureAllocation(() => Settings.ObservedFlag = NewExtensions.IsBrowsableZipFile(path, out _));
        Assert(newArchiveBytes == 0 && oldArchiveBytes > newArchiveBytes, "Expected archive allocation reduction was not observed.");
    });
    Test("solution detection clears stale selection for empty and unknown stored extensions", () =>
    {
        var current = new CurrentSolution { SolutionFilePath = "stale.sln" };
        current.Run();
        Assert(current.SolutionFilePath is null, "Empty collection retained a stale solution.");
        current.SolutionFilePath = "stale.sln";
        current.SetItems(new SolutionItem(null, "null"), new("", "empty"), new(".txt", "unknown"), new(".sln.note", "substring"), new("file.sln", "name-instead-of-extension"));
        ProbeDirectory.Calls = 0;
        current.Run();
        Assert(current.SolutionFilePath is null && ProbeDirectory.Calls == 0, "Unknown stored extensions matched or performed filesystem probes.");
    });
    Test("solution detection matches both stored extensions with ordinal case insensitivity", () =>
    {
        foreach (var extension in new[] { ".sln", ".SLN", ".sLn", ".slnx", ".SLNX", ".sLnX" })
        {
            var original = new OriginalSolution();
            original.SetItems(new SolutionItem(extension, "case-match"));
            original.Run();
            var current = new CurrentSolution();
            current.SetItems(new SolutionItem(extension, "case-match"));
            ProbeDirectory.Calls = 0;
            current.Run();
            Assert(current.SolutionFilePath == original.SolutionFilePath && current.SolutionFilePath == "case-match", $"Stored extension case changed: {extension}");
            Assert(ProbeDirectory.Calls == 0, "Stored extension match probed the filesystem.");
        }
    });
    Test("multiple solution matches select the first item in the current collection order", () =>
    {
        var current = new CurrentSolution();
        var first = new SolutionItem(".slnx", "first.slnx");
        var second = new SolutionItem(".sln", "second.sln");
        current.SetItems(new SolutionItem(".txt", "skip.txt"), first, second);
        current.Run();
        Assert(current.SolutionFilePath == first.ItemPath && second.ExtensionReads == 0, "First match or early exit was lost.");
        current.SetItems(second, first);
        current.Run();
        Assert(current.SolutionFilePath == second.ItemPath, "Reordered collection retained an earlier selection.");
    });
    Test("solution detection retains folder items with a stored solution extension", () =>
    {
        var original = new OriginalSolution();
        original.SetItems(new SolutionItem(".sln", "folder-path", StorageItemTypes.Folder));
        original.Run();
        var current = new CurrentSolution();
        current.SetItems(new SolutionItem(".sln", "folder-path", StorageItemTypes.Folder));
        current.Run();
        Assert(current.SolutionFilePath == "folder-path" && current.SolutionFilePath == original.SolutionFilePath, "An unrequested folder filter changed stored-extension behavior.");
    });
    Test("100000 nonmatches read protected stored extensions without filesystem classification", () =>
    {
        var items = Enumerable.Range(0, 100_000).Select(index => new SolutionItem(".txt", $"item-{index}" )).ToArray();
        var current = new CurrentSolution { SolutionFilePath = "stale.sln" };
        current.SetItems(items);
        ProbeDirectory.Calls = 0;
        current.Run();
        Assert(current.SolutionFilePath is null && ProbeDirectory.Calls == 0, "Large negative solution scan retained selection or probed the filesystem.");
        Assert(items.All(item => item.ExtensionReads == 2), "Large scan skipped entries or changed stored-extension checks.");
    });
    Test("CWD directories named .sln or .slnx cannot suppress a stored solution extension", () =>
    {
        foreach (var extension in new[] { ".sln", ".slnx" })
        {
            Directory.CreateDirectory(Path.Combine(fixture, extension));
            var original = new OriginalSolution();
            original.SetItems(new SolutionItem(extension, "solution-path"));
            ProbeDirectory.Calls = 0;
            original.Run();
            Assert(original.SolutionFilePath is null && ProbeDirectory.Calls > 0, "Original CWD dependency was not reproduced.");
            var current = new CurrentSolution();
            current.SetItems(new SolutionItem(extension, "solution-path"));
            ProbeDirectory.Calls = 0;
            current.Run();
            Assert(current.SolutionFilePath == "solution-path" && ProbeDirectory.Calls == 0, "Current selection depends on unrelated CWD artifacts.");
        }
    });
    Test("solution extension reads hold the collection lock and notification assignment releases it", () =>
    {
        var item = new SolutionItem(".sln", "guarded.sln");
        var current = new CurrentSolution();
        current.SetItems(item);
        current.Run();
        Assert(item.ExtensionReads > 0 && current.SolutionFilePath == "guarded.sln", "Protected collection was not inspected.");
        Assert(!current.SetterRanInsideCollectionLock, "Solution path assignment invoked downstream work inside the collection lock.");
    });
}
finally { Directory.SetCurrentDirectory(previousDirectory); }
var metadataPath = Path.Combine(AppContext.BaseDirectory, "source-metadata.json");
using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
var report = new
{
    generatedAtUtc = DateTime.UtcNow,
    scope = "Extracted real production members including CheckForSolutionFile and source helper with counted directory probes, protected stored-extension reads and confined fixtures; not complete GetFile, native traversal, sorting, WinUI, or user-visible latency.",
    source = metadata.RootElement.Clone(),
    results,
    allocations = new { iterations = 10_000, originalNameBytes = oldNameBytes, currentNameBytes = newNameBytes, originalArchiveBytes = oldArchiveBytes, currentArchiveBytes = newArchiveBytes }
};
using (var stream = new FileStream(Path.Combine(output, "entry-contracts.json"), FileMode.CreateNew)) JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
foreach (var result in results) Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")}: {result.Name} {result.Detail}");
Console.WriteLine(JsonSerializer.Serialize(report.allocations));
if (results.Any(result => !result.Passed)) Environment.ExitCode = 1;

void Test(string name, Action action)
{
    try { action(); results.Add(new(name, true, "")); }
    catch (Exception ex) { results.Add(new(name, false, ex.Message)); }
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static long MeasureAllocation(Action action)
{
    action();
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var iteration = 0; iteration < 10_000; iteration++) action();
    return GC.GetAllocatedBytesForCurrentThread() - before;
}
record ContractResult(string Name, bool Passed, string Detail);

namespace EntryConstruction.Contracts
{
    public enum StorageItemTypes { File, Folder }
    public readonly record struct FindData(string cFileName);
    public readonly record struct LinkKind(bool IsShortcut, bool IsUrl);
    public sealed class SolutionItem(string? extension, string? itemPath, StorageItemTypes kind = StorageItemTypes.File)
    {
        public object? RequiredLock { get; set; }
        public int ExtensionReads;
        public string? ItemPath { get; } = itemPath;
        public StorageItemTypes PrimaryItemAttribute { get; } = kind;
        public string? FileExtension
        {
            get
            {
                if (RequiredLock is not null && !System.Threading.Monitor.IsEntered(RequiredLock)) throw new InvalidOperationException("Stored extension was read outside the collection lock.");
                Interlocked.Increment(ref ExtensionReads);
                return extension;
            }
        }
    }
    public sealed class FakeFolderSettings { public bool ShowFileExtensions { get; set; } }
    public sealed class FakeUserSettings { public FakeFolderSettings FoldersSettingsService { get; } = new(); }
    public static class Settings
    {
        public static FakeUserSettings UserSettingsService { get; } = new();
        public static string? ObservedName;
        public static bool ObservedFlag;
    }
    public static class ProbeDirectory
    {
        public static int Calls;
        public static bool Exists(string? path) { Interlocked.Increment(ref Calls); return Directory.Exists(path); }
    }
    public static class ProbeIO
    {
        public static class Path
        {
            public static bool Exists(string? path) => System.IO.Path.Exists(path);
        }
    }
}
