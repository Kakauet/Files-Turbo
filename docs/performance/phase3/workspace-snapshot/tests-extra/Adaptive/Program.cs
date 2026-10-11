using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Files.Shared.Helpers;

var settings = new Settings();
var context = new Context();
var ini = new IniService();
Ioc.Default.Services[typeof(ILayoutSettingsService)] = settings;
Ioc.Default.Services[typeof(IContentPageContext)] = context;
Ioc.Default.Services[typeof(IWindowsIniService)] = ini;
var mediaExtensions = new[] { ".mp3", ".m4a", ".ogg", ".oga", ".wav", ".wma", ".aac", ".adt", ".adts", ".cda", ".flac", ".avi", ".mp4", ".webm", ".mov", ".qt", ".m4v", ".mp4v", ".3g2", ".3gp2", ".3gp", ".3gpp", ".mkv", ".png", ".bmp", ".jpg", ".jpeg", ".jfif", ".gif", ".tiff", ".tif", ".webp", ".jxr" };
var results = new List<object>();
int failures = 0;
var taskRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var syntheticRoot = Path.Combine(taskRoot, "benchmarks/fixtures/adaptive-contracts-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(syntheticRoot);
string previousDirectory = Environment.CurrentDirectory;
try
{
    Environment.CurrentDirectory = syntheticRoot;
    Test("EveryExistingAudioVideoImageExtension", () =>
    {
        foreach (string extension in mediaExtensions)
        {
            Equal(true, FileExtensionHelpers.IsAudioFile(extension) || FileExtensionHelpers.IsVideoFile(extension) || FileExtensionHelpers.IsImageFile(extension));
            Equal(FolderLayoutModes.GridView, Classify(new ListedItem { FileExtension = extension, PrimaryItemAttribute = StorageItemTypes.File }));
        }
    });
    Test("CaseInsensitiveExtensions", () =>
    {
        foreach (string extension in mediaExtensions)
            Equal(FolderLayoutModes.GridView, Classify(new ListedItem { FileExtension = extension.ToUpperInvariant() }));
    });
    Test("NonMediaAndUnknownExtensions", () =>
    {
        foreach (string? extension in new[] { null, "", ".txt", ".zip", ".ico", ".wmv", ".mpg", ".pdf", ".unknown", " " })
            Equal(FolderLayoutModes.DetailsView, Classify(new ListedItem { FileExtension = extension }));
    });
    Test("ExactSixtyPercentRemainsDetails", () => Equal(FolderLayoutModes.DetailsView, Classify(Mixed(60, 40))));
    Test("AboveSixtyPercentUsesGrid", () => Equal(FolderLayoutModes.GridView, Classify(Mixed(61, 39))));
    Test("EmptyDoesNotChangeLayout", () => Equal(FolderLayoutModes.ListView, Classify(Array.Empty<ListedItem>())));
    Test("FolderDenominatorAndStoredExtensionPreserved", () =>
    {
        Equal(FolderLayoutModes.DetailsView, Classify(new ListedItem { PrimaryItemAttribute = StorageItemTypes.Folder, FileExtension = null }));
        // Existing adaptive behavior classifies stored extensions, including a custom folder extension.
        Equal(FolderLayoutModes.GridView, Classify(new ListedItem { PrimaryItemAttribute = StorageItemTypes.Folder, FileExtension = ".jpg" }));
        Equal(FolderLayoutModes.DetailsView, Classify(Enumerable.Range(0, 3).Select(_ => new ListedItem { FileExtension = ".jpg" }).Concat(Enumerable.Range(0, 2).Select(_ => new ListedItem { PrimaryItemAttribute = StorageItemTypes.Folder })).ToArray()));
    });
    Test("ShortcutUsesOwnExtensionAndNotTarget", () =>
    {
        Equal(FolderLayoutModes.DetailsView, Classify(new ShortcutItem { FileExtension = ".lnk", TargetPath = "picture.jpg" }));
        Equal(FolderLayoutModes.DetailsView, Classify(new ShortcutItem { FileExtension = ".url", TargetPath = "video.mp4" }));
        Equal(FolderLayoutModes.GridView, Classify(new ShortcutItem { FileExtension = ".jpg", TargetPath = "document.txt" }));
    });
    Test("PrimaryFolderZipRemainsDetails", () => Equal(FolderLayoutModes.DetailsView, Classify(new ZipItem { PrimaryItemAttribute = StorageItemTypes.Folder, FileExtension = ".zip" })));
    Test("RelativeExtensionFolderDoesNotChangeKnownMetadata", () =>
    {
        Directory.CreateDirectory(".jpg");
        Equal(false, FileExtensionHelpers.IsImageFile(".jpg"));
        Equal(FolderLayoutModes.GridView, Classify(new ListedItem { FileExtension = ".jpg" }));
        Equal(false, FileExtensionHelpers.IsImageFile(Path.GetFullPath(".jpg")));
    });
    Test("DesktopIniPicturesAndVideosOverrideCount", () =>
    {
        foreach (string mode in new[] { "Pictures", "Videos" })
        {
            context.ShellPage!.ShellViewModel!.DesktopIni = Sections(mode);
            Equal(FolderLayoutModes.GridView, Classify(new ListedItem { FileExtension = ".txt" }));
        }
    });
    Test("DesktopIniOtherModeRemainsDetails", () =>
    {
        context.ShellPage!.ShellViewModel!.DesktopIni = Sections("Documents");
        Equal(FolderLayoutModes.DetailsView, Classify(new ListedItem { FileExtension = ".jpg" }));
    });
    Test("UserFixedSharedAndDisabledSettingsPreserved", () =>
    {
        var item = new[] { new ListedItem { FileExtension = ".jpg" } };
        var preferences = new LayoutPreferencesManager { IsLayoutModeFixed = true };
        AdaptiveLayoutHelpers.ApplyAdaptativeLayout(preferences, item); Equal(0, preferences.Toggles);
        preferences.IsLayoutModeFixed = false; preferences.IsAdaptiveLayoutEnabled = false;
        AdaptiveLayoutHelpers.ApplyAdaptativeLayout(preferences, item); Equal(0, preferences.Toggles);
        preferences.IsAdaptiveLayoutEnabled = true; settings.SyncFolderPreferencesAcrossDirectories = true;
        AdaptiveLayoutHelpers.ApplyAdaptativeLayout(preferences, item); Equal(0, preferences.Toggles);
    });
    Test("AlreadyChosenLayoutDoesNotToggle", () =>
    {
        var preferences = new LayoutPreferencesManager { LayoutMode = FolderLayoutModes.GridView };
        AdaptiveLayoutHelpers.ApplyAdaptativeLayout(preferences, new[] { new ListedItem { FileExtension = ".jpg" } });
        Equal(0, preferences.Toggles);
    });
    Test("PredictionUnknownPathFallsBack", () =>
    {
        Equal(false, AdaptiveLayoutHelpers.TryPredictLayout(null, out _));
        Equal(false, AdaptiveLayoutHelpers.TryPredictLayout(Path.Combine(syntheticRoot, "missing"), out _));
    });
    Test("PredictionUsesSameExtensionSet", () =>
    {
        var directory = Path.Combine(syntheticRoot, "predict"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "image.JPEG"), "synthetic");
        File.WriteAllText(Path.Combine(directory, "audio.FLAC"), "synthetic");
        File.WriteAllText(Path.Combine(directory, "video.MKV"), "synthetic");
        Equal(true, AdaptiveLayoutHelpers.TryPredictLayout(directory, out var layout)); Equal(FolderLayoutModes.GridView, layout);
    });
    Test("HundredThousandKnownExtensionsNoItemPathAccess", () =>
    {
        var items = Mixed(61000, 39000);
        var watch = Stopwatch.StartNew();
        Equal(FolderLayoutModes.GridView, Classify(items));
        watch.Stop();
        Console.WriteLine($"100000 metadata entries classified in {watch.Elapsed.TotalMilliseconds:F3}ms (contract process; not UI timing).");
    });
}
finally
{
    Environment.CurrentDirectory = previousDirectory;
    // The synthetic fixture is retained as evidence; this harness never deletes user data.
}
var sourcePath = Path.Combine(taskRoot, "source/src/Files.App/Helpers/Layout/AdaptiveLayoutHelpers.cs");
var report = new { TimestampUtc = DateTime.UtcNow, ProductionSource = sourcePath, SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))), Passed = results.Count - failures, Failed = failures, Results = results, SyntheticFixture = syntheticRoot, Scope = "Full linked AdaptiveLayoutHelpers; UI/service/ListedItem dependencies substituted; no WinUI frame claim" };
File.WriteAllText(args.Length > 0 ? args[0] : "adaptive-contracts.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Adaptive contracts: {results.Count - failures}/{results.Count} passed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

void Test(string name, Action run)
{
    context.ShellPage!.ShellViewModel!.DesktopIni = null;
    settings.SyncFolderPreferencesAcrossDirectories = false;
    ini.Sections = new();
    try { run(); results.Add(new { Name = name, Passed = true, Error = "" }); }
    catch (Exception ex) { failures++; results.Add(new { Name = name, Passed = false, Error = ex.Message }); Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static ListedItem[] Mixed(int media, int other) => Enumerable.Range(0, media + other).Select(i => new ListedItem { FileExtension = i < media ? ".jpg" : ".txt", PrimaryItemAttribute = StorageItemTypes.File }).ToArray();
static FolderLayoutModes Classify(params ListedItem[] items) { var preferences = new LayoutPreferencesManager(); AdaptiveLayoutHelpers.ApplyAdaptativeLayout(preferences, items); return preferences.LayoutMode; }
static List<IniSectionDataItem> Sections(string mode) => new() { new IniSectionDataItem { SectionName = "ViewState", Parameters = new Dictionary<string, string> { ["Mode"] = mode } } };
