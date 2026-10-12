// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	internal static class SearchSuggestionNarrowing
	{
		internal static bool CanReuse(string previous, string current, string? previousFolder, string? folder)
			=> previous.Length > 0 && current.Length > previous.Length &&
				current.StartsWith(previous, StringComparison.OrdinalIgnoreCase) && previousFolder == folder &&
				current.AsSpan().IndexOfAny("*?:$".AsSpan()) < 0;

		internal static List<T> Filter<T>(IEnumerable<T> items, string query, Func<T, string?> name)
			=> items.Where(item => name(item)?.Contains(query, StringComparison.OrdinalIgnoreCase) == true).ToList();
	}
}
