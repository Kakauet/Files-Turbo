global using Files.App.Helpers;
global using Files.App.Data.Contracts;
global using Files.App.Data.Items;
global using System.IO;

namespace Files.App.Data.Items
{
    public enum DriveType { Fixed, CDRom }
    public sealed class DriveItem
    {
        public DriveType Type { get; set; }
        public ByteSizeLib.ByteSize MaxSpace { get; set; }
        public string Path { get; set; } = @"C:\";
        public string GetRequiredPath() => Path;
    }
    public sealed class PathBoxItem { public string? Path { get; set; } }
}
namespace ByteSizeLib { public readonly record struct ByteSize(long Bytes) { public static ByteSize FromBytes(long bytes) => new(bytes); } }
namespace Files.App.Data.Contracts
{
    public interface IShellPage
    {
        ShellViewModel? ShellViewModel { get; }
        Type CurrentPageType { get; }
        void NavigateHome();
        void NavigateToReleaseNotes();
        void NavigateToSettings();
        void NavigateToPath(string path);
    }
}
namespace Files.App.Helpers
{
    public sealed class HomePage;
    public sealed class DetailsPage;
    public sealed class ShellViewModel { public string? WorkingDirectory { get; set; } = @"C:\A"; public bool IsSearchResults { get; set; } public bool IsWorkingDirectoryUpdatePending { get; set; } }
    public sealed class ShellPage : IShellPage
    {
        public ShellViewModel? ShellViewModel { get; set; } = new();
        public Type CurrentPageType { get; set; } = typeof(DetailsPage);
        public List<string> Navigations { get; } = new();
        public void NavigateHome() => NavigateToPath("Home");
        public void NavigateToReleaseNotes() => NavigateToPath("ReleaseNotes");
        public void NavigateToSettings() => NavigateToPath("Settings");
        public void NavigateToPath(string path) { Navigations.Add(path); ShellViewModel!.WorkingDirectory = path; }
    }
    public sealed class Context { public IShellPage? ShellPage { get; set; } }
    public sealed class DriveRegistry { public List<DriveItem> Drives { get; } = new(); }
    public sealed class Lookup<T> where T : class
    {
        public T? Result { get; set; }
        public string ErrorCode { get; set; } = "NotFound";
        public static implicit operator bool(Lookup<T> result) => result.Result is not null;
    }
    public sealed class StorageItem { public string Path { get; set; } = ""; }
    public static class FilesystemTasks
    {
        public static async Task<Lookup<T>> Wrap<T>(Func<Task<T?>> action) where T : class => new() { Result = await action() };
        public static Task<Lookup<T>> WrapNullable<T>(Func<Task<T?>> action) where T : class => Wrap(action);
    }
    public static class DriveHelpers
    {
        public static Func<string, Task<StorageItem?>> RootHandler { get; set; } = path => Task.FromResult<StorageItem?>(new() { Path = path });
        public static Task<StorageItem?> GetRootFromPathAsync(string path) => RootHandler(path);
        public static List<string> Ejected { get; } = new();
        public static void EjectDeviceAsync(string path) => Ejected.Add(path);
    }
    public static class StorageFileExtensions
    {
        public static Func<string, Task<StorageItem?>> FolderHandler { get; set; } = path => Task.FromResult<StorageItem?>(new() { Path = path });
        public static Func<string, Task<StorageItem?>> FileHandler { get; set; } = path => Task.FromResult<StorageItem?>(null);
        public static Func<string, string> ResolveHandler { get; set; } = path => path;
        public static string GetResolvedPath(string path, bool isFtp) => ResolveHandler(path);
        public static Task<StorageItem?> DangerousGetFolderWithPathFromPathAsync(string path, object root) => FolderHandler(path);
        public static Task<StorageItem?> DangerousGetFileWithPathFromPathAsync(string path, object root) => FileHandler(path);
    }
    public static class FolderHelpers { public static bool CheckFolderAccessWithWin32(string path) => false; }
    public static class FtpHelpers { public static bool IsFtpPath(string path) => path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase); }
    public static class PathNormalization { public static string NormalizePath(string path) => path; }
    public static class Win32Helper
    {
        public static List<string> Invoked { get; } = new();
        public static Task InvokeWin32ComponentAsync(string path, IShellPage owner) { Invoked.Add(path); return Task.CompletedTask; }
    }
    public static class DialogDisplayHelper
    {
        public static List<string> Shown { get; } = new();
        public static Func<Task<bool>> Handler { get; set; } = () => Task.FromResult(false);
        public static Task<bool> ShowDialogAsync(params string[] text) { Shown.Add(text[0]); return Handler(); }
    }
    public static class Constants { public static class UserEnvironmentPaths { public const string HomePath = "Home"; } }
    public static class Strings
    {
        public const string Home = "Home", ReleaseNotes = "ReleaseNotes", Settings = "Settings", InsertDiscDialogTitle = "InsertDisc",
            InsertDiscDialogText = "Insert {0}", InsertDiscDialog_OpenDriveButton = "Eject", Close = "Close", InvalidItemDialogTitle = "InvalidItem",
            InvalidItemDialogContent = "Invalid {0}{1}", InvalidCommand = "InvalidCommand", InvalidCommandContent = "Invalid {0}",
            CommandNotExecutable = "NotExecutable", CommandNotExecutableContent = "Not executable {0}";
    }
    public static class Localization { public static string GetLocalizedResource(this string text) => text; }
    public sealed class Command
    {
        public bool IsExecutable { get; set; } = true;
        public string Code { get; set; } = "known";
        public int Executed { get; private set; }
        public Task ExecuteAsync() { Executed++; return Task.CompletedTask; }
    }
    public sealed class CommandSet
    {
        public Command None { get; } = new();
        public Command Known { get; } = new();
        public Command this[string code] => code == "known" ? Known : None;
    }
}
namespace Windows.System
{
    public static class Launcher
    {
        public static List<string> Invoked { get; } = new();
        public static Func<Task<bool>> Handler { get; set; } = () => Task.FromResult(false);
        public static Task<bool> LaunchUriAsync(Uri uri) { Invoked.Add(uri.ToString()); return Handler(); }
    }
}
namespace Files.App.ViewModels.UserControls
{
    public partial class NavigationToolbarViewModel
    {
        private readonly LatestPathNavigationCoordinator<IShellPage> pathNavigationCoordinator = new();
        private bool _isDisposed;
        public Context ContentPageContext { get; } = new();
        public DriveRegistry drivesViewModel { get; } = new();
        public List<PathBoxItem> PathComponents { get; } = new();
        public string? PathText { get; set; } = "https://invalid.example";
        public string? PathControlDisplayText { get; set; }
        public CommandSet Commands { get; } = new();
        public List<string> History { get; } = new();
        public List<string> LaunchedApps { get; } = new();
        public Func<Task<bool>> LaunchAppHandler { get; set; } = () => Task.FromResult(false);
        private void SavePathToHistory(string path) => History.Add(path);
        private Task<bool> LaunchApplicationFromPath(string input, string? directory) { LaunchedApps.Add(input); return LaunchAppHandler(); }
        public void DisposeForContract() { _isDisposed = true; pathNavigationCoordinator.Invalidate(); }
    }
}
