// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Diagnostics;

namespace Files.App.Utils.Storage
{
	internal sealed class SearchSuggestionPipeline
	{
		private double lastSearchDurationMilliseconds;

		internal async Task RunAsync<T>(string query, Func<Task<IReadOnlyList<T>>> search,
			Func<bool> isCurrent, Action<IReadOnlyList<T>> publish,
			Func<IReadOnlyList<T>, CancellationToken, Task> enrich, CancellationToken token)
		{
			var delay = query.StartsWith('$') || query.Contains(':') || lastSearchDurationMilliseconds > 150
				? 200 : query.Length < 3 ? 160 : 80;
			await Task.Delay(delay, token);
			token.ThrowIfCancellationRequested();
			if (!isCurrent())
				return;

			var started = Stopwatch.GetTimestamp();
			var results = await search();
			token.ThrowIfCancellationRequested();
			if (!isCurrent())
				return;

			lastSearchDurationMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
			publish(results);
			token.ThrowIfCancellationRequested();
			if (isCurrent())
				await enrich(results, token);
		}
	}
}
