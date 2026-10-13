// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	internal sealed class SearchSuggestionPipeline
	{

		internal async Task RunAsync<T>(string query, Func<Task<IReadOnlyList<T>>> search,
			Func<bool> isCurrent, Action<IReadOnlyList<T>> publish,
			Func<IReadOnlyList<T>, CancellationToken, Task> enrich, CancellationToken token)
		{
			// Coalesce a fast burst without penalizing the next name query after a slow walk.
			var delay = query.StartsWith('$') || query.Contains(':') ? 200 : 20;
			await Task.Delay(delay, token);
			token.ThrowIfCancellationRequested();
			if (!isCurrent())
				return;

			var results = await search();
			token.ThrowIfCancellationRequested();
			if (!isCurrent())
				return;

			publish(results);
			token.ThrowIfCancellationRequested();
			if (isCurrent())
				await enrich(results, token);
		}
	}
}
