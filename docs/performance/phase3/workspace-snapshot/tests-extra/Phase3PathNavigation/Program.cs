using System.Security.Cryptography;
using System.Text.Json;
using Files.App.ViewModels.UserControls;

const string A = @"C:\A", B = @"C:\B", C = @"C:\C";
var results = new List<object>();
int failures = 0;
foreach (bool legacy in new[] { false, true })
{
    string mode = legacy ? "Legacy" : "Modern";
    await Test(mode + "DelayedRootFinalCurrentAInvalidatesB", async () =>
    {
        var (vm, shell) = Create(); var pending = NewPending();
        DriveHelpers.RootHandler = path => path == B ? pending.Task : Found(path);
        var old = Submit(vm, B, legacy);
        await Submit(vm, A, legacy);
        pending.SetResult(new() { Path = B }); await old;
        Equal(A, shell.ShellViewModel!.WorkingDirectory); Equal(0, shell.Navigations.Count); Equal(0, vm.History.Count);
    });
    await Test(mode + "CurrentAWhileDirectoryCommitPendingMustNavigateA", async () =>
    {
        var (vm, shell) = Create(); shell.ShellViewModel!.IsWorkingDirectoryUpdatePending = true;
        await Submit(vm, A, legacy, A);
        Equal(A, shell.Navigations.Single()); Equal(A, vm.History.Single());
    });
    await Test(mode + "DelayedFolderCannotOverwriteNewC", async () =>
    {
        var (vm, shell) = Create(); var pending = NewPending();
        StorageFileExtensions.FolderHandler = path => path == B ? pending.Task : Found(path);
        var old = Submit(vm, B, legacy); await Submit(vm, C, legacy);
        pending.SetResult(new() { Path = B }); await old;
        Equal(C, shell.ShellViewModel!.WorkingDirectory); Equal(1, shell.Navigations.Count); Equal(C, vm.History.Single());
    });
    await Test(mode + "RejectedBlankInvalidatesPendingB", async () =>
    {
        var (vm, shell) = Create(); var pending = NewPending();
        DriveHelpers.RootHandler = path => path == B ? pending.Task : Found(path);
        var old = Submit(vm, B, legacy); await Submit(vm, " ", legacy);
        pending.SetResult(new() { Path = B }); await old;
        Equal(A, shell.ShellViewModel!.WorkingDirectory); Equal(0, shell.Navigations.Count);
    });
    await Test(mode + "StaleFailedFolderDoesNotLaunchOrDialog", async () =>
    {
        var (vm, shell) = Create(); var pending = NewPending(); int fileCalls = 0;
        StorageFileExtensions.FolderHandler = path => path == B ? pending.Task : Found(path);
        StorageFileExtensions.FileHandler = path => { fileCalls++; return Task.FromResult<StorageItem?>(null); };
        var old = Submit(vm, B, legacy); await Submit(vm, C, legacy);
        pending.SetResult(null); await old;
        Equal(C, shell.ShellViewModel!.WorkingDirectory); Equal(0, fileCalls); Equal(0, vm.LaunchedApps.Count); Equal(0, DialogDisplayHelper.Shown.Count);
    });
    await Test(mode + "StaleFileLookupCannotLaunch", async () =>
    {
        var (vm, shell) = Create(); var pending = NewPending();
        StorageFileExtensions.FolderHandler = path => path == B ? Task.FromResult<StorageItem?>(null) : Found(path);
        StorageFileExtensions.FileHandler = path => pending.Task;
        var old = Submit(vm, B, legacy); await Submit(vm, C, legacy);
        pending.SetResult(new() { Path = B }); await old;
        Equal(C, shell.ShellViewModel!.WorkingDirectory); Equal(0, Win32Helper.Invoked.Count); Equal(0, vm.LaunchedApps.Count);
    });
    await Test(mode + "StaleAppFailureCannotLaunchUri", async () =>
    {
        var (vm, shell) = Create(); var pending = new TaskCompletionSource<bool>();
        StorageFileExtensions.FolderHandler = path => path == B ? Task.FromResult<StorageItem?>(null) : Found(path);
        vm.LaunchAppHandler = () => pending.Task;
        var old = Submit(vm, B, legacy); await Submit(vm, C, legacy);
        pending.SetResult(false); await old;
        Equal(C, shell.ShellViewModel!.WorkingDirectory); Equal(0, Windows.System.Launcher.Invoked.Count); Equal(0, DialogDisplayHelper.Shown.Count);
    });
    await Test(mode + "StaleUriFailureCannotShowError", async () =>
    {
        var (vm, shell) = Create(); var pending = new TaskCompletionSource<bool>();
        StorageFileExtensions.FolderHandler = path => path == B ? Task.FromResult<StorageItem?>(null) : Found(path);
        Windows.System.Launcher.Handler = () => pending.Task;
        var old = Submit(vm, B, legacy); await Submit(vm, C, legacy);
        pending.SetResult(false); await old;
        Equal(C, shell.ShellViewModel!.WorkingDirectory); Equal(0, DialogDisplayHelper.Shown.Count);
    });
    await Test(mode + "OwnerChangeDiscardsPendingValidation", async () =>
    {
        var (vm, shell) = Create(); var pending = NewPending();
        DriveHelpers.RootHandler = path => pending.Task;
        var old = Submit(vm, B, legacy); var newShell = new ShellPage(); vm.ContentPageContext.ShellPage = newShell;
        pending.SetResult(new() { Path = B }); await old;
        Equal(0, shell.Navigations.Count); Equal(0, newShell.Navigations.Count); Equal(0, vm.History.Count);
    });
    await Test(mode + "DisposeDiscardsPendingValidation", async () =>
    {
        var (vm, shell) = Create(); var pending = NewPending();
        DriveHelpers.RootHandler = path => pending.Task;
        var old = Submit(vm, B, legacy); vm.DisposeForContract();
        pending.SetResult(new() { Path = B }); await old;
        Equal(0, shell.Navigations.Count); Equal(0, vm.History.Count);
    });
    await Test(mode + "StaleBreadcrumbDoesNotDropNewPath", async () =>
    {
        var (vm, shell) = Create(); shell.ShellViewModel!.WorkingDirectory = C;
        vm.PathComponents.Add(new() { Path = A });
        await Submit(vm, A, legacy, A);
        Equal(A, shell.ShellViewModel.WorkingDirectory); Equal(A, shell.Navigations.Single());
    });
    await Test(mode + "SlashAndUncNormalizationPreserved", async () =>
    {
        var (vm, shell) = Create(); await Submit(vm, "C:/B", legacy); Equal(B, shell.Navigations.Single());
        await Submit(vm, @"\\server\share", legacy); Equal(@"\\server\share", shell.Navigations.Last());
    });
    await Test(mode + "FtpFallbackStillNavigates", async () =>
    {
        var (vm, shell) = Create(); StorageFileExtensions.FolderHandler = path => Task.FromResult<StorageItem?>(null);
        await Submit(vm, "ftp://host/music", legacy); Equal("ftp://host/music", shell.Navigations.Single()); Equal(0, vm.LaunchedApps.Count);
    });
    await Test(mode + "FileLaunchStillWorksForCurrentRequest", async () =>
    {
        var (vm, shell) = Create(); StorageFileExtensions.FolderHandler = path => Task.FromResult<StorageItem?>(null);
        StorageFileExtensions.FileHandler = Found;
        await Submit(vm, B, legacy); Equal(B, Win32Helper.Invoked.Single()); Equal(0, shell.Navigations.Count);
    });
    await Test(mode + "RejectedCurrentInputStillShowsError", async () =>
    {
        var (vm, shell) = Create(); StorageFileExtensions.FolderHandler = path => Task.FromResult<StorageItem?>(null);
        await Submit(vm, B, legacy); Equal(1, DialogDisplayHelper.Shown.Count); Equal(0, shell.Navigations.Count);
    });
}
await Test("ModernPendingInvalidatedByLegacyNoop", async () =>
{
    var (vm, shell) = Create(); var pending = NewPending(); DriveHelpers.RootHandler = path => pending.Task;
    var old = vm.HandleItemNavigationAsync(B); await vm.CheckPathInputAsync(A, A, shell);
    pending.SetResult(new() { Path = B }); await old; Equal(0, shell.Navigations.Count);
});
await Test("LegacyPendingInvalidatedByModernNoop", async () =>
{
    var (vm, shell) = Create(); var pending = NewPending(); DriveHelpers.RootHandler = path => pending.Task;
    var old = vm.CheckPathInputAsync(B, A, shell); await vm.HandleItemNavigationAsync(A);
    pending.SetResult(new() { Path = B }); await old; Equal(0, shell.Navigations.Count);
});
await Test("ModernSupersededDialogDoesNotReturnFailure", async () =>
{
    var (vm, shell) = Create(); var pending = new TaskCompletionSource<bool>();
    StorageFileExtensions.FolderHandler = path => path == B ? Task.FromResult<StorageItem?>(null) : Found(path);
    DialogDisplayHelper.Handler = () => pending.Task;
    var old = vm.HandleItemNavigationAsync(B); await vm.HandleItemNavigationAsync(C);
    pending.SetResult(false); Equal(true, await old); Equal(C, vm.PathControlDisplayText);
});
await Test("LegacyCommandExecutionPreservedAndInvalidatesOlderPath", async () =>
{
    var (vm, shell) = Create(); var pending = NewPending(); DriveHelpers.RootHandler = path => pending.Task;
    var old = vm.HandleItemNavigationAsync(B); await vm.CheckPathInputAsync(">known", A, shell);
    pending.SetResult(new() { Path = B }); await old; Equal(1, vm.Commands.Known.Executed); Equal(0, shell.Navigations.Count);
});
await Test("ModernSameDirectorySearchExitPreserved", async () =>
{
    var (vm, shell) = Create(); shell.ShellViewModel!.IsSearchResults = true;
    await vm.HandleItemNavigationAsync(A); Equal(A, shell.Navigations.Single());
});
await Test("ModernResolvedAliasNoopInvalidatesOlderRequest", async () =>
{
    var (vm, shell) = Create(); var pending = NewPending(); DriveHelpers.RootHandler = path => pending.Task;
    var old = vm.HandleItemNavigationAsync(B); StorageFileExtensions.ResolveHandler = path => path == "%alias%" ? A : path;
    await vm.HandleItemNavigationAsync("%alias%"); pending.SetResult(new() { Path = B }); await old; Equal(0, shell.Navigations.Count);
});
await Test("LegacyRejectedCommandPreserved", async () =>
{
    var (vm, shell) = Create(); await vm.CheckPathInputAsync(">unknown", A, shell);
    Equal(Strings.InvalidCommand, DialogDisplayHelper.Shown.Single()); Equal(0, vm.Commands.Known.Executed);
});

var taskRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string toolbarRelative = "source/src/Files.App/ViewModels/UserControls/NavigationToolbarViewModel.cs";
string coordinatorRelative = "source/src/Files.App/Helpers/Navigation/LatestPathNavigationCoordinator.cs";
string toolbarHash = Hash(Path.Combine(taskRoot, toolbarRelative));
string extractedHash = File.ReadAllText(Path.Combine(taskRoot, "tests-extra/Phase3PathNavigation/source-hash.txt")).Trim();
if (toolbarHash != extractedHash) throw new Exception("Production toolbar changed after extraction; regenerate and rebuild contracts.");
var evidence = new { TimestampUtc = DateTime.UtcNow, Passed = results.Count - failures, Failed = failures, Results = results,
    ProductionSources = new[] { new { Path = toolbarRelative, Sha256 = toolbarHash }, new { Path = coordinatorRelative, Sha256 = Hash(Path.Combine(taskRoot, coordinatorRelative)) } },
    ExtractedToolbarSha256 = extractedHash, GeneratedMethodsSha256 = Hash(Path.Combine(taskRoot, "tests-extra/Phase3PathNavigation/PathCore.generated.cs")),
    Scope = "Literal production modern/legacy path methods and normalization, linked coordinator; controlled root/folder/file/launch/UI dependencies; no physical frame measurement" };
File.WriteAllText(args.Length > 0 ? args[0] : "path-navigation-contracts.json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Path navigation contracts: {results.Count - failures}/{results.Count} passed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

async Task Test(string name, Func<Task> run)
{
    Reset();
    try { await run(); results.Add(new { Name = name, Passed = true, Error = "" }); }
    catch (Exception ex) { failures++; results.Add(new { Name = name, Passed = false, Error = ex.Message }); Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static TaskCompletionSource<StorageItem?> NewPending() => new();
static Task<StorageItem?> Found(string path) => Task.FromResult<StorageItem?>(new() { Path = path });
static Task Submit(NavigationToolbarViewModel vm, string path, bool legacy, string? selected = A) => legacy ? vm.CheckPathInputAsync(path, selected, vm.ContentPageContext.ShellPage!) : vm.HandleItemNavigationAsync(path);
static (NavigationToolbarViewModel, ShellPage) Create() { var vm = new NavigationToolbarViewModel(); var shell = new ShellPage(); vm.ContentPageContext.ShellPage = shell; return (vm, shell); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void Reset()
{
    DriveHelpers.RootHandler = Found; DriveHelpers.Ejected.Clear();
    StorageFileExtensions.FolderHandler = Found; StorageFileExtensions.FileHandler = path => Task.FromResult<StorageItem?>(null); StorageFileExtensions.ResolveHandler = path => path;
    DialogDisplayHelper.Shown.Clear(); DialogDisplayHelper.Handler = () => Task.FromResult(false);
    Win32Helper.Invoked.Clear(); Windows.System.Launcher.Invoked.Clear(); Windows.System.Launcher.Handler = () => Task.FromResult(false);
}
