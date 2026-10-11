// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace Files.App.Utils.Storage
{
	internal static class ParallelDirectoryWork
	{
		internal static async Task RunAsync((string Path, bool UseFindTimeout) root, int concurrency,
			Action<(string Path, bool UseFindTimeout), Action<(string Path, bool UseFindTimeout)>, CancellationToken> process, CancellationToken token)
		{
			using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
			using var folders = new BlockingCollection<(string Path, bool UseFindTimeout)>();
			var remaining = 1;
			folders.Add(root);
			void Enqueue((string Path, bool UseFindTimeout) folder)
			{
				cancellation.Token.ThrowIfCancellationRequested();
				Interlocked.Increment(ref remaining);
				folders.Add(folder, cancellation.Token);
			}

			var workers = Enumerable.Range(0, concurrency).Select(_ => Task.Factory.StartNew(() =>
			{
				try
				{
					foreach (var folder in folders.GetConsumingEnumerable(cancellation.Token))
					{
						try { process(folder, Enqueue, cancellation.Token); }
						finally
						{
							if (Interlocked.Decrement(ref remaining) == 0)
								folders.CompleteAdding();
						}
					}
				}
				catch
				{
					cancellation.Cancel();
					throw;
				}
			}, cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
			await Task.WhenAll(workers).ConfigureAwait(false);
		}
	}
}
