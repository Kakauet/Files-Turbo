// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage
{
	internal static class StorageEnumerationTask
	{
		public static async Task<T?> AwaitAsync<T>(Task<T> task, TimeSpan timeout, CancellationToken token, Action<T> cleanup)
		{
			try
			{
				return await task.WaitAsync(timeout, token);
			}
			catch (TimeoutException)
			{
				_ = CleanupAbandonedResultAsync(task, cleanup);
				return default;
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				_ = CleanupAbandonedResultAsync(task, cleanup);
				throw;
			}
		}

		private static async Task CleanupAbandonedResultAsync<T>(Task<T> task, Action<T> cleanup)
		{
			try
			{
				cleanup(await task.ConfigureAwait(false));
			}
			catch
			{
				// Observe abandoned native failures; the current location no longer owns this request.
			}
		}
	}
}
