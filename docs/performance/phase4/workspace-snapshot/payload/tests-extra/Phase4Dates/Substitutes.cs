global using System.Collections;
global using System.Diagnostics;
global using Files.App.Helpers;
global using Files.App.Utils;
global using Files.App.Utils.Storage;

namespace WinRT { [AttributeUsage(AttributeTargets.Class)] public sealed class GeneratedWinRTExposedTypeAttribute : Attribute { } }
namespace Files.App { public static class App { public static StubLogger Logger { get; } = new(); } public sealed class StubLogger { public void LogDebug(string message) { } } }
namespace Files.App.Utils
{
    public partial class ListedItem
    {
        protected static readonly Files.App.Services.DateTimeFormatter.CountingFormatter dateTimeFormatter = new();
        public static Files.App.Services.DateTimeFormatter.CountingFormatter Formatter => dateTimeFormatter;
        public int Notifications { get; private set; }
        public Action<string>? NotificationCallback { get; set; }
        public virtual string? Name { get; set; }
        protected void OnPropertyChanged(string property) { Notifications++; NotificationCallback?.Invoke(property); }
        protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string property = "") { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(property); return true; }
    }
}
namespace Files.App.ViewModels
{
    public partial class ShellViewModel
    {
        private readonly ConcurrentCollection<ListedItem> filesAndFolders;
        public ShellViewModel(IEnumerable<ListedItem> items) => filesAndFolders = new(items);
    }
}
namespace Files.App.Views.Layouts
{
    public sealed class SelectorItem { public ListedItem? Content { get; set; } }
    public sealed class StubArgs { public uint Phase { get; set; } }
    public sealed class StubShellPage { public Files.App.ViewModels.ShellViewModel? ShellViewModel { get; set; } }
    public partial class BaseLayoutPage
    {
        private readonly Dictionary<SelectorItem, ListedItem> realizedItems = new(ReferenceEqualityComparer.Instance);
        private bool isDisposed;
        private object? navigationArguments = new();
        public StubShellPage? ParentShellPageInstance { get; set; }
        public void Track(SelectorItem container, ListedItem item) { realizedItems[container] = item; container.Content = item; }
        public void Recycle(SelectorItem container) { realizedItems.Remove(container); container.Content = null; }
        public void Dispose() => isDisposed = true;
        public void ClearNavigation() => navigationArguments = null;
    }
}
namespace Files.App.Services.DateTimeFormatter
{
    internal abstract class AbstractDateTimeFormatter
    {
        public abstract string Name { get; }
        public abstract string ToShortLabel(DateTimeOffset offset);
        public virtual string ToLongLabel(DateTimeOffset offset) => ToShortLabel(offset);
        protected static string ToString(DateTimeOffset offset, string format) => offset.ToLocalTime().ToString(format, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    }
    public sealed class CountingFormatter
    {
        private readonly ApplicationDateTimeFormatter implementation = new();
        public long Calls;
        public string Prefix = "";
        public string ToShortLabel(DateTimeOffset offset) { Calls++; var label = implementation.ToShortLabel(offset); return Prefix.Length == 0 ? label : Prefix + label; }
    }
    internal enum Strings { Application, DaysAgo_Plural, DaysAgo_Singular, HoursAgo_Plural, HoursAgo_Singular, MinutesAgo_Plural, MinutesAgo_Singular, SecondsAgo_Plural, SecondsAgo_Singular, Now }
    internal static class ResourceExtensions
    {
        public static string GetLocalizedResource(this Strings value) => value switch { Strings.Application => "Application", Strings.DaysAgo_Plural => "{0} days ago", Strings.DaysAgo_Singular => "1 day ago", Strings.HoursAgo_Plural => "{0} hours ago", Strings.HoursAgo_Singular => "1 hour ago", Strings.MinutesAgo_Plural => "{0} minutes ago", Strings.MinutesAgo_Singular => "1 minute ago", Strings.SecondsAgo_Plural => "{0} seconds ago", Strings.SecondsAgo_Singular => "1 second ago", _ => "Now" };
    }
}
