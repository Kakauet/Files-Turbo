// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	internal static class OperationSourceMatch
	{
		/// <summary>
		/// For each result path, the source with the same path (ignoring case); results without a source are skipped.
		/// </summary>
		/// <remarks>
		/// Replaces a SingleOrDefault scan of the sources per result, which re-evaluated lazy source queries and was
		/// quadratic in the number of items. When two sources share a path the first one is used.
		/// </remarks>
		public static List<T> Match<T>(IEnumerable<string> resultPaths, IEnumerable<T> sources, Func<T, string> getPath)
		{
			var byPath = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
			foreach (var source in sources)
				byPath.TryAdd(getPath(source), source);

			var matches = new List<T>();
			foreach (var path in resultPaths)
			{
				if (byPath.TryGetValue(path, out var source))
					matches.Add(source);
			}

			return matches;
		}
	}
}
