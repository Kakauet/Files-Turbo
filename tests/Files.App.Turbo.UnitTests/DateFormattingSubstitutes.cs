// Test-only localization values for the linked production date formatters.
namespace Files.App
{
	internal static partial class Strings
	{
		public const string Future="future", Today="today", Yesterday="yesterday", EarlierThisWeek="week", LastWeek="last week", EarlierThisMonth="month", LastMonth="last month", EarlierThisYear="year", LastYear="last year", YearN="{0}";
		public const string SystemTimeStyle="system", Universal="universal", Application="application";
		public const string DaysAgo_Plural="{0} days", DaysAgo_Singular="yesterday", HoursAgo_Plural="{0} hours", HoursAgo_Singular="hour", MinutesAgo_Plural="{0} minutes", MinutesAgo_Singular="minute", SecondsAgo_Plural="{0} seconds", SecondsAgo_Singular="second", Now="now";
	}
}
namespace Files.App.Helpers
{
	internal static class AppLanguageHelper
	{
		internal sealed record Language(string Code);
		public static Language PreferredLanguage { get; } = new("es-ES");
	}
}