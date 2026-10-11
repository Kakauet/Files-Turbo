global using Files.App.Helpers;
global using Files.App.Data.Items;
global using Files.App.ViewModels;
global using System.Collections.ObjectModel;

namespace Files.App.Data.Items
{
    public sealed record StorageFolderWithPath(string Path);
    public sealed class WorkingDirectoryModifiedEventArgs : EventArgs
    {
        public string? Path { get; set; }
        public bool IsLibrary { get; set; }
        public string? Name { get; set; }
    }
    public sealed class PathBoxItem
    {
        public string? Path { get; set; }
        public string? Title { get; set; }
        public string? ChevronToolTip { get; set; }
    }
}

namespace Files.App.Helpers
{
    public static class FilesystemTasks
    {
        public static Task<StorageFolderWithPath?> WrapNullable(Func<Task<StorageFolderWithPath?>> action) => action();
    }
    public static class DriveHelpers
    {
        public static Func<string, Task<StorageFolderWithPath?>> RootHandler { get; set; } = path => Task.FromResult<StorageFolderWithPath?>(new(System.IO.Path.GetPathRoot(path)!));
        public static Task<StorageFolderWithPath?> GetRootFromPathAsync(string path) => RootHandler(path);
    }
    public static class GitHelpers
    {
        public static Func<string, string?, string?> RepositoryHandler { get; set; } = (path, root) => "repo:" + path;
        public static Func<string?, Task<string?>> HeadHandler { get; set; } = _ => Task.FromResult<string?>("main");
        public static string? GetGitRepositoryPath(string path, string? root) => RepositoryHandler(path, root);
        public static Task<string?> GetRepositoryHeadName(string? path) => HeadHandler(path);
    }
    public static class FtpHelpers
    {
        public static bool IsFtpPath(string value) => value.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase);
    }
    public static class NavigationHelpers
    {
        public static int IconCalls;
        public static Func<string, Task<string?>> IconHandler { get; set; } = path => Task.FromResult<string?>("icon:" + path);
        public static Task<string?> GetIconForPathAsync(string path) { Interlocked.Increment(ref IconCalls); return IconHandler(path); }
    }
    public sealed class LibraryItem { public string Text { get; set; } = "Library"; }
    public sealed class LibraryManager
    {
        public bool TryGetLibrary(string path, out LibraryItem library)
        {
            library = new();
            return path.StartsWith("library:", StringComparison.Ordinal);
        }
    }
    public sealed class JumpListService { public Task AddFolderAsync(string path) => Task.CompletedTask; }
    public static class StorageFileExtensions
    {
        public static Func<string, Task<IReadOnlyList<PathBoxItem>>> ComponentsHandler { get; set; } = path => Task.FromResult<IReadOnlyList<PathBoxItem>>([new() { Path = path, Title = path }]);
        public static Task<IReadOnlyList<PathBoxItem>> GetDirectoryPathComponentsWithDisplayNameAsync(string path) => ComponentsHandler(path);
    }
    public static class Strings { public const string BreadcrumbBarChevronButtonToolTip = "Open {0}"; }
    public static class Localization { public static string GetLocalizedResource(this string value) => value; }
    public sealed class ToolbarViewModel
    {
        public ObservableCollection<PathBoxItem> PathComponents { get; } = new();
        private bool isSingleItemOverride;
        public Action? OverrideChanged { get; set; }
        public bool IsSingleItemOverride { get => isSingleItemOverride; set { isSingleItemOverride = value; OverrideChanged?.Invoke(); } }
        public string? PathControlDisplayText { get; set; }
    }
    public sealed class NavigationArguments
    {
        public string? NavPathParam { get; set; }
        public object? AssociatedTabInstance { get; set; }
    }
    public sealed class SuppressNavigationTransitionInfo;
    public sealed class DetailsPage;
    public sealed class LayoutPreferencesManager
    {
        public bool IsLayoutModeChanging { get; set; } = true;
        public Type GetLayoutType(string path) => typeof(DetailsPage);
    }
    public sealed class InstanceViewModel { public LayoutPreferencesManager FolderSettings { get; } = new(); }
    public sealed class TabParameter { public object? NavigationParameter { get; set; } }
    public sealed class Frame
    {
        public List<NavigationArguments> Navigations { get; } = new();
        public Func<NavigationArguments, Task>? NavigateHandler { get; set; }
        public Task LastNavigateTask { get; private set; } = Task.CompletedTask;
        public void Navigate(Type? pageType, NavigationArguments arguments, SuppressNavigationTransitionInfo transition)
        {
            Navigations.Add(arguments);
            LastNavigateTask = NavigateHandler?.Invoke(arguments) ?? Task.CompletedTask;
        }
    }
    public static class TestState
    {
        public static void Reset()
        {
            DriveHelpers.RootHandler = path => Task.FromResult<StorageFolderWithPath?>(new(System.IO.Path.GetPathRoot(path)!));
            GitHelpers.RepositoryHandler = (path, root) => "repo:" + path;
            GitHelpers.HeadHandler = _ => Task.FromResult<string?>("main");
            NavigationHelpers.IconCalls = 0;
            NavigationHelpers.IconHandler = path => Task.FromResult<string?>("icon:" + path);
            StorageFileExtensions.ComponentsHandler = path => Task.FromResult<IReadOnlyList<PathBoxItem>>([new() { Path = path, Title = path }]);
        }
    }
}

namespace Files.App
{
    public static class App { public static LibraryManager LibraryManager { get; } = new(); }
}

namespace Files.App.ViewModels
{
    public partial class ShellViewModel
    {
        private long workingDirectoryRevision;
        private string? requestedWorkingDirectory = @"C:\A";
        private bool isDisposed;
        private StorageFolderWithPath? workingRoot = new(@"C:\");
        private StorageFolderWithPath? currentStorageFolder = new(@"C:\A");
        private readonly JumpListService jumpListService = new();
        public event EventHandler<WorkingDirectoryModifiedEventArgs>? WorkingDirectoryModified;
        public string WorkingDirectory { get; set; } = @"C:\A";
        public string? GitDirectory { get; set; } = "initial-repo";
        public bool IsValidGitDirectory { get; set; }
        private object? filter = new();
        public Action? FilterChanged { get; set; }
        public object? FilesAndFoldersFilter { get => filter; set { filter = value; FilterChanged?.Invoke(); } }
        public bool ShowFilterHeader => FilesAndFoldersFilter is not null;
        public List<string> PropertyNotifications { get; } = new();
        public int ThumbnailUpdates => NavigationHelpers.IconCalls;
        public string? FolderThumbnailImageSource { get; set; } = "initial-icon";
        public string? RootPath => workingRoot?.Path;
        public string? CurrentFolderPath => currentStorageFolder?.Path;
        public void DisposeForTest() => isDisposed = true;
        private void OnPropertyChanged(string name) => PropertyNotifications.Add(name);
    }
}

namespace Files.App.Views.Shells
{
    public partial class BaseShellPage
    {
        private long pathUiRevision;
        private bool isDisposed;
        public ToolbarViewModel ToolbarViewModel { get; } = new();
        public void DisposeForTest() => isDisposed = true;
        public virtual void NavigateToPath(string? navigationPath, Type? sourcePageType, NavigationArguments? navArgs = null) { }
    }
    public partial class ModernShellPage : BaseShellPage
    {
        public ShellViewModel? ShellViewModel { get; set; } = new();
        public InstanceViewModel InstanceViewModel { get; } = new();
        public TabParameter? TabBarItemParameter { get; set; }
        public Frame ItemDisplayFrame { get; } = new();
    }
}
