global using SystemIO = System.IO;
global using Files.App.Data.Enums;
global using Files.App.Data.Items;
global using Files.App.Helpers;
global using Files.App.Utils;

namespace Files.App.ViewModels.Previews { public sealed class NamespaceMarker; }

namespace Files.App.Utils
{
    public enum StorageItemTypes { None, File, Folder }
    public class ListedItem
    {
        public StorageItemTypes PrimaryItemAttribute { get; set; }
        public string? FileExtension { get; set; }
        public bool IsFolder => PrimaryItemAttribute is StorageItemTypes.Folder;
        public string? ItemPath => throw new InvalidOperationException("Classification must use known extension, never inspect the item path.");
    }
    public sealed class ShortcutItem : ListedItem { public string? TargetPath { get; set; } }
    public sealed class ZipItem : ListedItem;
}

namespace Files.App.Helpers
{
    public interface ILayoutSettingsService { bool SyncFolderPreferencesAcrossDirectories { get; } }
    public interface IContentPageContext { ShellPage? ShellPage { get; } }
    public interface IWindowsIniService { List<IniSectionDataItem> GetData(string path); }
    public sealed class ShellPage { public ShellViewModel? ShellViewModel { get; set; } }
    public sealed class ShellViewModel { public List<IniSectionDataItem>? DesktopIni { get; set; } }
    public sealed class LayoutPreferencesManager
    {
        public bool IsLayoutModeFixed { get; set; }
        public bool IsAdaptiveLayoutEnabled { get; set; } = true;
        public FolderLayoutModes LayoutMode { get; set; } = FolderLayoutModes.ListView;
        public int Toggles { get; private set; }
        public void ToggleLayoutModeDetailsView(bool persist) { LayoutMode = FolderLayoutModes.DetailsView; Toggles++; }
        public void ToggleLayoutModeGridView(bool persist) { LayoutMode = FolderLayoutModes.GridView; Toggles++; }
    }
    public sealed class Ioc
    {
        public static Ioc Default { get; } = new();
        public Dictionary<Type, object> Services { get; } = new();
        public T GetRequiredService<T>() => (T)Services[typeof(T)];
    }
    public sealed class Settings : ILayoutSettingsService { public bool SyncFolderPreferencesAcrossDirectories { get; set; } }
    public sealed class Context : IContentPageContext { public ShellPage? ShellPage { get; set; } = new() { ShellViewModel = new() }; }
    public sealed class IniService : IWindowsIniService
    {
        public List<IniSectionDataItem> Sections { get; set; } = new();
        public List<IniSectionDataItem> GetData(string path) => Sections;
    }
}
