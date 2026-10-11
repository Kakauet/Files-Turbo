global using System.Collections.Concurrent;

namespace Files.App
{
    public static class App
    {
        public static Files.App.Utils.Storage.App.StubLogger Logger => Files.App.Utils.Storage.App.Logger;
        public static Files.App.Utils.Storage.App.StubLibraryManager LibraryManager => Files.App.Utils.Storage.App.LibraryManager;
    }
}

namespace Files.App.Utils.Storage
{
    public enum EmptyTextType { None }
    public sealed class FolderSearch
    {
        public string Query { get; set; } = "query";
        public bool HasIncompleteResults { get; set; }
        public object? DispatcherQueue { get; set; }
        public event EventHandler<IReadOnlyList<ListedItem>>? SearchTick;
        public Func<List<ListedItem>, CancellationToken, Task>? SearchOverride { get; set; }
        public Task SearchAsync(List<ListedItem> results, CancellationToken token) => SearchOverride?.Invoke(results, token) ?? Task.CompletedTask;
        public void Tick(params ListedItem[] items) => SearchTick?.Invoke(this, items);
        public bool HasTickSubscribers => SearchTick is not null;
    }
    public static class UIHelpers { public static Task<object> GetSearchIconResource() => Task.FromResult(new object()); }
    public enum GroupOption { None, Size }
    public static class ShellLibraryItem { public const string EXTENSION = ".library-ms"; }
    public static class AppMemoryHelper { public static int Requests; public static void NotifyActivity() {} public static void RequestTrim() => Interlocked.Increment(ref Requests); }
    public static class AdaptiveLayoutHelpers { public static void ApplyAdaptativeLayout(ShellFolderSettings settings, List<ListedItem> items) { } }
    public sealed class ShellFolderSettings
    {
        public GroupOption DirectoryGroupOption { get; set; }
        public SortOption DirectorySortOption { get; set; } = SortOption.Name;
        public SortDirection DirectorySortDirection { get; set; } = SortDirection.Ascending;
        public bool SortDirectoriesAlongsideFiles { get; set; }
        public bool SortFilesFirst { get; set; }
    }
}
namespace Files.App.ViewModels
{
    public sealed class DeferredDispatcher
    {
        public bool HasThreadAccess => true;
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task EnqueueOrInvokeAsync(Action action)
        {
            Posted.TrySetResult();
            if (Gate is not null) await Gate.Task;
            action();
        }
    }
    public sealed class ItemLoadStatusChangedEventArgs : EventArgs
    {
        public enum ItemLoadStatus { Starting, InProgress, Complete }
        public ItemLoadStatus Status { get; set; }
        public string? PreviousDirectory { get; set; }
        public string? Path { get; set; }
    }
    public partial class ShellViewModel
    {
        private const int MaxSearchResultsAppendedPerTick = 64;
        private bool isDisposed;
        private readonly object loadCollectionSync = new();
        public long PerformanceOperationId { get; set; }
        private CancellationTokenSource addFilesCTS = new();
        private CancellationTokenSource loadPropsCTS = new();
        private TaskCompletionSource? scrollSettledTcs;
        private readonly ConcurrentDictionary<ListedItem, (CancellationToken Token, bool Canceled)> itemLoadQueue = new();
        public void TestBeginPropertyLoad(ListedItem item, CancellationToken token) => itemLoadQueue[item] = (token, false);
        public bool TestCompletePropertyLoad(ListedItem item, CancellationToken token, bool canceled) => CompleteItemPropertyLoad(item, token, canceled);
        public int PendingPropertyCount => itemLoadQueue.Count;
        private readonly ConcurrentDictionary<ListedItem, (CancellationTokenSource Source, CancellationToken Token)> viewportPropertyCancellation = new();
        public CancellationToken TestPropertyToken(ListedItem item, CancellationToken token) => GetPropertyLoadToken(item, token);
        public int PropertyLifetimeCount => viewportPropertyCancellation.Count;
        private CancellationTokenSource semaphoreCTS = new();
        private CancellationTokenSource? searchCTS;
        private readonly IIconCacheService iconCacheService = Ioc.Default;
        private readonly SemaphoreSlim enumFolderSemaphore = new(1, 1);
        private readonly SemaphoreSlim bulkOperationSemaphore = new(1, 1);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> thumbnailRetryDebounce = new();
        private ConcurrentCollection<ListedItem> filesAndFolders = new();
        private readonly Queue<ListedItem> pendingSearchResults = new();
        private Task? desktopIniUpdateTask;
        private readonly ShellFolderSettings folderSettings = new();
        public DeferredDispatcher dispatcherQueue { get; } = new();
        public BulkConcurrentObservableCollection<ListedItem> FilesAndFolders { get; } = new();
        public bool IsSearchResults { get; set; }
        public bool HasNoWatcher { get; set; }
        public bool IsLocationUnavailable { get; set; }
        public bool IsNetworkDiscoveryInfoBarOpen { get; set; }
        public bool IsLoadingItems { get; set; }
        public EmptyTextType EmptyTextType { get; set; }
        public string SearchHeaderTitle { get; set; } = "";
        public object? SearchIconBitmapImage { get; set; } = new();
        public CancellationTokenSource? SearchSource => searchCTS;
        public string? FilesAndFoldersFilter { get; set; }
        public string WorkingDirectory { get; set; } = "C";
        public event EventHandler<ItemLoadStatusChangedEventArgs>? ItemLoadStatusChanged;
        public event EventHandler? DirectoryInfoUpdated;
        public Func<string, CancellationToken, Task>? EnumerationOverride { get; set; }
        public Func<CancellationToken, bool, Task>? ThumbnailOverride { get; set; }
        public int PendingRetryCount => thumbnailRetryDebounce.Count;
        public (Task Task, CancellationTokenSource Source) ScheduleRetry(CancellationToken parent, int delayMs = 0)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(parent);
            var item = new ListedItem { ItemNameRaw = "retry", NeedsDelayedThumbnailLoad = true };
            var token = source.Token;
            thumbnailRetryDebounce[item.GetRequiredPath()] = source;
            return (RetryThumbnailAfterDelayAsync(item, source, token, delayMs), source);
        }
        public List<string> EnumerationCalls { get; } = new();
        public void Seed(params string[] names)
        {
            lock (loadCollectionSync)
            {
                filesAndFolders.AddRange(names.Select(name => new ListedItem { ItemNameRaw = name }));
                FilesAndFolders.AddRange(filesAndFolders.ToList());
            }
        }
        public string?[] LogicalNames => filesAndFolders.Select(x => x.Name).ToArray();
        public Task LoadAsync(string path, Action? callback = null) => RapidAddItemsToCollectionAsync(path, null, callback);
        public Task SortAsync() => OrderFilesAndFoldersAsync();
        public void CloseWatcher() { }
        public void CancelNetworkAvailabilityUpdate() { }
        public void StopWatchingForLocationRestoration() { }
        private void OrderGroups() { }
        private void UpdateEmptyTextType() { }
        private void UpdateNetworkAvailabilityInfoBar() { }
        private uint GetPreloadIconSize() => 32;
        private Task LoadThumbnailAsync(ListedItem item, CancellationToken token, bool scheduleTimerRetry = true, bool awaitBackgroundLoad = false) => ThumbnailOverride?.Invoke(token, awaitBackgroundLoad) ?? Task.CompletedTask;
        private static bool MatchesFilter(ListedItem item, string? filter) => string.IsNullOrEmpty(filter) || item.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
        private async Task RapidAddItemsToCollectionAsync(string? path, CancellationToken token, LibraryItem? library = null)
        {
            EnumerationCalls.Add(path!);
            if (EnumerationOverride is not null) await EnumerationOverride(path!, token);
            lock (loadCollectionSync)
            {
                token.ThrowIfCancellationRequested();
                filesAndFolders.Add(new ListedItem { ItemNameRaw = path });
            }
            await OrderFilesAndFoldersAsync(token);
            await ApplyFilesAndFoldersChangesAsync(token);
        }
    }
}

namespace Files.App.Helpers { internal static class UiPerformanceTrace { public static bool IsEnabled => false; public static long BeginOperation(string kind, string path) => 0; public static void Mark(long operationId, string milestone, int count = 0, double durationMs = 0, long value = 0) {} } }
