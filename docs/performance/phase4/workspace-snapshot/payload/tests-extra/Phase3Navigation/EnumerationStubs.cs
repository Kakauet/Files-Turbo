global using System.Collections;
global using System.ComponentModel;
global using System.Diagnostics;
global using Files.App.Helpers;
global using Files.App.Utils;
global using Files.App.Utils.Storage;
global using Files.Shared.Extensions;
global using Windows.Storage;

namespace WinRT
{
    [AttributeUsage(AttributeTargets.Class)] internal sealed class GeneratedWinRTExposedTypeAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)] internal sealed class GeneratedWinRTExposedExternalTypeAttribute(Type type) : Attribute { }
}
namespace Microsoft.UI.Xaml.Media.Imaging { public sealed class BitmapImage { } }
namespace Microsoft.Extensions.Logging { }
namespace Windows.Storage
{
    public enum StorageItemTypes { File, Folder }
    public interface IStorageItem { string Name { get; } bool IsOfType(StorageItemTypes type); }
}
namespace Files.App.Services.SizeProvider
{
    public interface ISizeProvider { bool TryGetSize(string path, out ulong size); Task UpdateAsync(string path, CancellationToken token); }
}
namespace Files.Shared.Helpers
{
    public static class PathNormalization { public static string Combine(string root, string name) => Path.Combine(root, name); }
}
namespace Files.App.Utils
{
    public class ListedItem(string? id = null)
    {
        public StorageItemTypes PrimaryItemAttribute { get; set; }
        public string? ItemNameRaw { get; set; }
        public string? Name => ItemNameRaw;
        public string? ItemPath { get; set; }
        public DateTimeOffset ItemDateModifiedReal { get; set; }
        public DateTimeOffset ItemDateCreatedReal { get; set; }
        public string? ItemType { get; set; }
        public string? FileExtension { get; set; }
        public string? FileSize { get; set; }
        public long FileSizeBytes { get; set; }
        public object? FileImage { get; set; }
        public bool IsHiddenItem { get; set; }
        public double Opacity { get; set; }
        public bool LoadFileIcon { get; set; }
        public byte[]? PreloadedIconData { get; set; }
        public bool IsFolder => PrimaryItemAttribute == StorageItemTypes.Folder;
        public bool NeedsDelayedThumbnailLoad { get; set; }
        public bool ItemPropertiesInitialized { get; set; }
        public bool IsShortcut => this is ShortcutItem;
        public bool IsArchive => false;
        public string? SyncStatusString { get; set; }
        public string[]? FileTags { get; set; }
    }
    public class ShortcutItem(string? id) : ListedItem(id)
    {
        public string? TargetPath { get; set; } public string? Arguments { get; set; }
        public string? WorkingDirectory { get; set; } public bool RunAsAdmin { get; set; }
        public int ShowWindowCommand { get; set; } public bool IsUrl { get; set; }
    }
    public class RecycleBinItem(string? id) : ListedItem(id)
    {
        public DateTimeOffset ItemDateDeletedReal { get; set; }
        public string? ItemOriginalPath { get; set; }
        public string? ItemOriginalFolder => Path.GetDirectoryName(ItemOriginalPath);
    }
    public class LibraryItem(object library) : ListedItem { }
}
namespace Files.App.Utils.Storage
{
    public enum SortOption { Name, DateModified, DateCreated, FileType, Size, SyncStatus, FileTag, Path, OriginalFolder, DateDeleted }
    public enum SortDirection { Ascending, Descending }
    public sealed class NaturalStringComparer : IComparer<object?>
    {
        public static Action? BeforeCompare;
        public static NaturalStringComparer GetForProcessor() => new();
        public int Compare(object? x, object? y) { BeforeCompare?.Invoke(); return StringComparer.OrdinalIgnoreCase.Compare(x as string, y as string); }
    }
    public sealed class GroupedHeaderViewModel
    {
        public string? Key { get; set; } public string Text { get; set; } = "";
        public string? CountText { get; set; } public bool Initialized { get; set; }
        public void PausePropertyChangedNotifications() { }
        public void ResumePropertyChangedNotifications(bool triggerUpdates = true) { }
    }
    public static class Strings
    {
        public const string GroupItemsCount_Plural = "{0} items";
        public const string GroupItemsCount_Singular = "{0} item";
        public const string SearchResultsFor = "Results for {0}";
        public const string SearchResultsIncomplete = "Results may be incomplete.";
    }
    public static class StubExtensions
    {
        public static string GetLocalizedResource(this string value) => value;
        public static string ToSizeString(this ulong value) => value.ToString();
        public static string ToSizeString(this long value) => value.ToString();
        public static BaseStorageFolder? AsBaseStorageFolder(this IStorageItem item) => item as BaseStorageFolder;
        public static BaseStorageFile? AsBaseStorageFile(this IStorageItem item) => item as BaseStorageFile;
        public static string GetRequiredPath(this ListedItem item) => item.ItemPath ?? item.Name!;
    }
    public sealed record BasicProperties(ulong Size, DateTimeOffset DateModified);
    public class BaseStorageFile(FileInfo? info = null) : IStorageItem
    {
        public virtual string Name => info?.Name ?? "stub.txt";
        public virtual string Path => info?.FullName ?? Name;
        public string FolderRelativeId => Path;
        public string DisplayType => FileType;
        public string FileType => System.IO.Path.GetExtension(Name);
        public DateTimeOffset DateCreated => info is null ? DateTimeOffset.UnixEpoch : info.CreationTimeUtc;
        public virtual Task<BasicProperties> GetBasicPropertiesAsync() => Task.FromResult(new BasicProperties((ulong)(info?.Length ?? 0), info is null ? DateTimeOffset.UnixEpoch : info.LastWriteTimeUtc));
        public bool IsOfType(StorageItemTypes type) => type == StorageItemTypes.File;
    }
    public class BaseStorageFolder : IStorageItem, IDisposable
    {
        public virtual string Name => "folder"; public virtual string Path => Name;
        public string FolderRelativeId => Path; public string DisplayName => Name;
        public string DisplayType => "Folder"; public DateTimeOffset DateCreated => DateTimeOffset.UnixEpoch;
        public virtual Task<IReadOnlyList<IStorageItem>> GetItemsAsync(uint start, uint count) => Task.FromResult<IReadOnlyList<IStorageItem>>([]);
        public Task<BasicProperties> GetBasicPropertiesAsync() => Task.FromResult(new BasicProperties(0, DateTimeOffset.UnixEpoch));
        public bool IsOfType(StorageItemTypes type) => type == StorageItemTypes.Folder;
        public virtual void Dispose() { }
    }
    public sealed class DirectoryFolder(string path) : BaseStorageFolder
    {
        private readonly IEnumerator<FileInfo> enumerator = new DirectoryInfo(path).EnumerateFiles().GetEnumerator();
        public int Fetches { get; private set; }
        public override string Path => path;
        public override Task<IReadOnlyList<IStorageItem>> GetItemsAsync(uint start, uint count)
        {
            Fetches++;
            var items = new List<IStorageItem>((int)count);
            while (items.Count < count && enumerator.MoveNext()) items.Add(new BaseStorageFile(enumerator.Current));
            return Task.FromResult<IReadOnlyList<IStorageItem>>(items);
        }
        public override void Dispose() => enumerator.Dispose();
    }
    public class ShortcutStorageFolder : BaseStorageFolder
    {
        public string? TargetPath => null; public string? Arguments => null; public string? WorkingDirectory => null;
        public bool RunAsAdmin => false; public int ShowWindowCommand => 0;
    }
    public class ShortcutStorageFile : BaseStorageFile
    {
        public string? TargetPath => null; public string? Arguments => null; public string? WorkingDirectory => null;
        public bool RunAsAdmin => false; public int ShowWindowCommand => 0;
    }
    public class BinStorageFolder : BaseStorageFolder { public DateTimeOffset DateDeleted => DateTimeOffset.UnixEpoch; public string? OriginalPath => null; }
    public class BinStorageFile : BaseStorageFile { public DateTimeOffset DateDeleted => DateTimeOffset.UnixEpoch; public string? OriginalPath => null; }
    public sealed record StorageFolderWithPath(BaseStorageFolder Item, string Path);
    public static class FolderHelpers { public static bool CheckFolderAccessWithWin32(string path) => false; }
    public static class FileExtensionHelpers { public static bool IsWebLinkFile(string name) => name.EndsWith(".url"); }
    public interface IUserSettingsService { FolderSettings FoldersSettingsService { get; } }
    public sealed class FolderSettings { public bool CalculateFolderSizes => false; public bool ShowDotFiles => true; }
    public interface IIconCacheService { Task<byte[]?> GetIconAsync(string? path, string? extension, bool folder, uint size); }
    public sealed class Ioc : IUserSettingsService, IIconCacheService, Files.App.Services.SizeProvider.ISizeProvider
    {
        public static Func<Task<byte[]?>>? IconOverride;
        public static Ioc Default { get; } = new(); public FolderSettings FoldersSettingsService { get; } = new();
        public T GetRequiredService<T>() => (T)(object)this;
        public Task<byte[]?> GetIconAsync(string? path, string? extension, bool folder, uint size) => IconOverride?.Invoke() ?? Task.FromResult<byte[]?>(null);
        public bool TryGetSize(string path, out ulong size) { size = 0; return false; }
        public Task UpdateAsync(string path, CancellationToken token) => Task.CompletedTask;
    }
    public static class App
    {
        public static StubLogger Logger { get; } = new(); public static StubLibraryManager LibraryManager { get; } = new();
        public sealed class StubLogger { public void LogWarning(Exception ex, string message) { } }
        public sealed class StubLibrary { public bool IsEmpty => true; public string[] Folders => []; }
        public sealed class StubLibraryManager { public bool TryGetLibrary(string path, out StubLibrary library) { library = null!; return false; } }
    }
}
