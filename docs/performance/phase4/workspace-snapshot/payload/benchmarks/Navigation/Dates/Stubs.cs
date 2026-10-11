global using Files.App.Services.DateTimeFormatter;
global using Files.App.Utils;
global using System.ComponentModel;
global using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Files.App.Utils
{
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? property = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value; OnPropertyChanged(property); return true;
        }
        protected void OnPropertyChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    }
    public sealed class CountingFormatter
    {
        public static CountingFormatter Shared { get; } = new();
        public IDateTimeFormatter Formatter { get; set; } = new SystemDateTimeFormatter();
        public int Calls { get; set; }
        public string ToShortLabel(DateTimeOffset value) { Calls++; return Formatter.ToShortLabel(value); }
    }
}
namespace Files.App.Services.DateTimeFormatter
{
    public enum GroupByDateUnit { Day, Month, Year }
    public static class AppLanguageHelper { public static Language PreferredLanguage { get; } = new(); public sealed class Language { public string Code => "es-ES"; } }
    public static class Strings
    {
        public const string Application = "Application", SystemTimeStyle = "System", Universal = "Universal", Future = "Future", Today = "Today", Yesterday = "Yesterday", EarlierThisWeek = "This week", LastWeek = "Last week", EarlierThisMonth = "This month", LastMonth = "Last month", EarlierThisYear = "This year", LastYear = "Last year", YearN = "{0}", DaysAgo_Plural = "{0} days", DaysAgo_Singular = "1 day", HoursAgo_Plural = "{0} hours", HoursAgo_Singular = "1 hour", MinutesAgo_Plural = "{0} minutes", MinutesAgo_Singular = "1 minute", SecondsAgo_Plural = "{0} seconds", SecondsAgo_Singular = "1 second", Now = "Now";
    }
    public static class LocalizationStub { public static string GetLocalizedResource(this string text) => text; }
}
